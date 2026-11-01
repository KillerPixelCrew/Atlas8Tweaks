// VrrTest - minimal DX12 variable-refresh-rate test.
//
// Renders moving bars at a limited, adjustable frame rate in a borderless
// fullscreen flip-model swap chain and measures, independently of the app's
// own frame rate, how often the display actually refreshes:
//   - a background thread timestamps every IDXGIOutput::WaitForVBlank
//   - DXGI frame statistics give refreshes per presented frame
// With working VRR the refresh rate follows the frame rate. A fixed-refresh
// display stays at its maximum, and low-framerate compensation shows up as an
// integer multiple of the frame rate.
//
// Keys: Up/Down +-1 fps, Left/Right +-10 fps, 1-7 presets, S sweep 30-120,
//       M toggle present mode, Esc quit.
// Command line: --auto  steps through a fixed list of frame rates, writes
//               vrrtest-log.txt next to the executable and exits.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <timeapi.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "winmm.lib")

using Microsoft::WRL::ComPtr;

static const UINT kBufferCount = 3;

// ---------------------------------------------------------------- timing ----

static int64_t g_qpcFreq = 0;

static int64_t Now()
{
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);
    return t.QuadPart;
}

static double ToSeconds(int64_t ticks) { return double(ticks) / double(g_qpcFreq); }

static HANDLE g_waitTimer = nullptr;

// Sleeps on a high-resolution timer until ~1 ms before the target, then spins.
static void WaitUntil(int64_t target)
{
    for (;;) {
        int64_t remaining = target - Now();
        if (remaining <= 0)
            return;
        double ms = ToSeconds(remaining) * 1000.0;
        if (ms > 1.5 && g_waitTimer) {
            LARGE_INTEGER due;
            due.QuadPart = -LONGLONG((ms - 1.0) * 10000.0);
            SetWaitableTimerEx(g_waitTimer, &due, 0, nullptr, nullptr, nullptr, 0);
            WaitForSingleObject(g_waitTimer, INFINITE);
        } else {
            YieldProcessor();
        }
    }
}

// ----------------------------------------------------------------- state ----

static HWND g_hwnd = nullptr;
static UINT g_width = 0, g_height = 0;
static bool g_tearingSupported = false;

static ComPtr<ID3D12Device> g_device;
static ComPtr<ID3D12CommandQueue> g_queue;
static ComPtr<IDXGISwapChain3> g_swapChain;
static ComPtr<ID3D12DescriptorHeap> g_rtvHeap;
static UINT g_rtvStride = 0;
static ComPtr<ID3D12Resource> g_backBuffers[kBufferCount];
static ComPtr<ID3D12CommandAllocator> g_allocators[kBufferCount];
static ComPtr<ID3D12GraphicsCommandList> g_cmdList;
static ComPtr<ID3D12Fence> g_fence;
static UINT64 g_fenceValues[kBufferCount] = {};
static UINT64 g_fenceCounter = 0;
static HANDLE g_fenceEvent = nullptr;
static HANDLE g_latencyWaitable = nullptr;
static ComPtr<IDXGIOutput> g_output;

static double g_targetFps = 40.0;
static bool g_sweep = false;
static bool g_syncIntervalMode = false; // false: sync 0 + tearing, true: sync 1
static bool g_quit = false;

static std::atomic<bool> g_vblankRunning{true};
static std::mutex g_vblankMutex;
static std::deque<int64_t> g_vblanks;     // QPC timestamps of display vblanks
static std::deque<int64_t> g_presents;    // QPC timestamps of Present calls

struct StatsSample {
    UINT presentCount;
    UINT syncRefreshCount;
    int64_t syncQpc;
};
static std::deque<StatsSample> g_stats;

struct Metrics {
    double appFps = 0;
    double vblankHz = 0;
    double refreshPerFrame = 0; // from DXGI frame statistics, 0 if unavailable
    double statsHz = 0;
};

// ------------------------------------------------------------- 5x7 font ----

static const unsigned char* Glyph(char c)
{
    static const unsigned char digits[10][7] = {
        {0x0E,0x11,0x13,0x15,0x19,0x11,0x0E}, {0x04,0x0C,0x04,0x04,0x04,0x04,0x0E},
        {0x0E,0x11,0x01,0x02,0x04,0x08,0x1F}, {0x1F,0x02,0x04,0x02,0x01,0x11,0x0E},
        {0x02,0x06,0x0A,0x12,0x1F,0x02,0x02}, {0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E},
        {0x06,0x08,0x10,0x1E,0x11,0x11,0x0E}, {0x1F,0x01,0x02,0x04,0x08,0x08,0x08},
        {0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E}, {0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C},
    };
    static const unsigned char letters[26][7] = {
        {0x0E,0x11,0x11,0x1F,0x11,0x11,0x11}, {0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E},
        {0x0E,0x11,0x10,0x10,0x10,0x11,0x0E}, {0x1C,0x12,0x11,0x11,0x11,0x12,0x1C},
        {0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F}, {0x1F,0x10,0x10,0x1E,0x10,0x10,0x10},
        {0x0E,0x11,0x10,0x17,0x11,0x11,0x0F}, {0x11,0x11,0x11,0x1F,0x11,0x11,0x11},
        {0x0E,0x04,0x04,0x04,0x04,0x04,0x0E}, {0x07,0x02,0x02,0x02,0x02,0x12,0x0C},
        {0x11,0x12,0x14,0x18,0x14,0x12,0x11}, {0x10,0x10,0x10,0x10,0x10,0x10,0x1F},
        {0x11,0x1B,0x15,0x15,0x11,0x11,0x11}, {0x11,0x11,0x19,0x15,0x13,0x11,0x11},
        {0x0E,0x11,0x11,0x11,0x11,0x11,0x0E}, {0x1E,0x11,0x11,0x1E,0x10,0x10,0x10},
        {0x0E,0x11,0x11,0x11,0x15,0x12,0x0D}, {0x1E,0x11,0x11,0x1E,0x14,0x12,0x11},
        {0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E}, {0x1F,0x04,0x04,0x04,0x04,0x04,0x04},
        {0x11,0x11,0x11,0x11,0x11,0x11,0x0E}, {0x11,0x11,0x11,0x11,0x11,0x0A,0x04},
        {0x11,0x11,0x11,0x15,0x15,0x15,0x0A}, {0x11,0x11,0x0A,0x04,0x0A,0x11,0x11},
        {0x11,0x11,0x11,0x0A,0x04,0x04,0x04}, {0x1F,0x01,0x02,0x04,0x08,0x10,0x1F},
    };
    static const unsigned char dot[7]    = {0,0,0,0,0,0x0C,0x0C};
    static const unsigned char colon[7]  = {0,0x0C,0x0C,0,0x0C,0x0C,0};
    static const unsigned char minus[7]  = {0,0,0,0x1F,0,0,0};
    static const unsigned char slash[7]  = {0,0x01,0x02,0x04,0x08,0x10,0};
    static const unsigned char equal[7]  = {0,0,0x1F,0,0x1F,0,0};
    static const unsigned char lparen[7] = {0x02,0x04,0x08,0x08,0x08,0x04,0x02};
    static const unsigned char rparen[7] = {0x08,0x04,0x02,0x02,0x02,0x04,0x08};
    static const unsigned char plus[7]   = {0,0x04,0x04,0x1F,0x04,0x04,0};

    if (c >= '0' && c <= '9') return digits[c - '0'];
    if (c >= 'a' && c <= 'z') c = char(c - 'a' + 'A');
    if (c >= 'A' && c <= 'Z') return letters[c - 'A'];
    switch (c) {
    case '.': return dot;
    case ':': return colon;
    case '-': return minus;
    case '/': return slash;
    case '=': return equal;
    case '(': return lparen;
    case ')': return rparen;
    case '+': return plus;
    default: return nullptr;
    }
}

// Appends one rectangle per horizontal pixel run of the text.
static void AddText(std::vector<D3D12_RECT>& rects, int x, int y, int scale, const std::string& text)
{
    for (char c : text) {
        const unsigned char* g = Glyph(c);
        if (g) {
            for (int row = 0; row < 7; ++row) {
                int col = 0;
                while (col < 5) {
                    if (g[row] & (0x10 >> col)) {
                        int start = col;
                        while (col < 5 && (g[row] & (0x10 >> col)))
                            ++col;
                        rects.push_back({x + start * scale, y + row * scale,
                                         x + col * scale, y + (row + 1) * scale});
                    } else {
                        ++col;
                    }
                }
            }
        }
        x += 6 * scale;
    }
}

// --------------------------------------------------------------- vblanks ----

static void VBlankThread()
{
    while (g_vblankRunning) {
        if (FAILED(g_output->WaitForVBlank())) {
            Sleep(1);
            continue;
        }
        int64_t t = Now();
        std::lock_guard<std::mutex> lock(g_vblankMutex);
        g_vblanks.push_back(t);
        while (g_vblanks.size() > 4000)
            g_vblanks.pop_front();
    }
}

static Metrics ComputeMetrics(double windowSeconds)
{
    Metrics m;
    int64_t now = Now();
    int64_t from = now - int64_t(windowSeconds * g_qpcFreq);

    auto rate = [&](const std::deque<int64_t>& times) {
        size_t first = times.size();
        for (size_t i = 0; i < times.size(); ++i)
            if (times[i] >= from) { first = i; break; }
        if (times.size() - first < 2)
            return 0.0;
        double span = ToSeconds(times.back() - times[first]);
        return span > 0 ? double(times.size() - first - 1) / span : 0.0;
    };

    m.appFps = rate(g_presents);
    {
        std::lock_guard<std::mutex> lock(g_vblankMutex);
        m.vblankHz = rate(g_vblanks);
    }

    const StatsSample* first = nullptr;
    for (const StatsSample& s : g_stats)
        if (s.syncQpc >= from) { first = &s; break; }
    if (first && g_stats.size() >= 2) {
        const StatsSample& last = g_stats.back();
        UINT dp = last.presentCount - first->presentCount;
        UINT dr = last.syncRefreshCount - first->syncRefreshCount;
        double dt = ToSeconds(last.syncQpc - first->syncQpc);
        if (dp > 0 && dt > 0) {
            m.refreshPerFrame = double(dr) / double(dp);
            m.statsHz = double(dr) / dt;
        }
    }
    return m;
}

static std::string Verdict(const Metrics& m)
{
    if (m.appFps < 1 || m.vblankHz < 1)
        return "MEASURING...";
    double ratio = m.vblankHz / m.appFps;
    if (std::fabs(ratio - 1.0) < 0.04 && m.appFps < 115)
        return "VRR ACTIVE - REFRESH FOLLOWS FRAME RATE";
    double k = std::round(ratio);
    if (k >= 2 && std::fabs(ratio - k) < 0.08 && m.vblankHz < 117)
        return "VRR ACTIVE WITH LFC X" + std::to_string(int(k));
    if (m.vblankHz >= 117 && m.appFps < 110)
        return "FIXED REFRESH - VRR NOT ACTIVE";
    if (m.appFps >= 115)
        return "AT PANEL MAXIMUM";
    return "UNCLEAR - REFRESH DOES NOT TRACK FRAMES";
}

// ------------------------------------------------------------------ d3d12 ----

static void Check(HRESULT hr, const char* what)
{
    if (FAILED(hr)) {
        char msg[256];
        snprintf(msg, sizeof(msg), "%s failed: 0x%08X", what, unsigned(hr));
        MessageBoxA(nullptr, msg, "VrrTest", MB_ICONERROR);
        ExitProcess(1);
    }
}

static void InitD3D()
{
    ComPtr<IDXGIFactory5> factory;
    Check(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)), "CreateDXGIFactory2");
    BOOL tearing = FALSE;
    if (SUCCEEDED(factory->CheckFeatureSupport(DXGI_FEATURE_PRESENT_ALLOW_TEARING, &tearing, sizeof(tearing))))
        g_tearingSupported = tearing == TRUE;

    Check(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&g_device)), "D3D12CreateDevice");

    D3D12_COMMAND_QUEUE_DESC qd = {};
    qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    Check(g_device->CreateCommandQueue(&qd, IID_PPV_ARGS(&g_queue)), "CreateCommandQueue");

    DXGI_SWAP_CHAIN_DESC1 sd = {};
    sd.Width = g_width;
    sd.Height = g_height;
    sd.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.SampleDesc.Count = 1;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.BufferCount = kBufferCount;
    sd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    sd.Flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT |
               (g_tearingSupported ? DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING : 0);
    ComPtr<IDXGISwapChain1> sc1;
    Check(factory->CreateSwapChainForHwnd(g_queue.Get(), g_hwnd, &sd, nullptr, nullptr, &sc1), "CreateSwapChainForHwnd");
    factory->MakeWindowAssociation(g_hwnd, DXGI_MWA_NO_ALT_ENTER);
    Check(sc1.As(&g_swapChain), "IDXGISwapChain3");
    g_swapChain->SetMaximumFrameLatency(1);
    g_latencyWaitable = g_swapChain->GetFrameLatencyWaitableObject();
    Check(g_swapChain->GetContainingOutput(&g_output), "GetContainingOutput");

    D3D12_DESCRIPTOR_HEAP_DESC hd = {};
    hd.NumDescriptors = kBufferCount;
    hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    Check(g_device->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&g_rtvHeap)), "CreateDescriptorHeap");
    g_rtvStride = g_device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    D3D12_CPU_DESCRIPTOR_HANDLE rtv = g_rtvHeap->GetCPUDescriptorHandleForHeapStart();
    for (UINT i = 0; i < kBufferCount; ++i) {
        Check(g_swapChain->GetBuffer(i, IID_PPV_ARGS(&g_backBuffers[i])), "GetBuffer");
        g_device->CreateRenderTargetView(g_backBuffers[i].Get(), nullptr, rtv);
        rtv.ptr += g_rtvStride;
        Check(g_device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&g_allocators[i])), "CreateCommandAllocator");
    }
    Check(g_device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g_allocators[0].Get(), nullptr, IID_PPV_ARGS(&g_cmdList)), "CreateCommandList");
    g_cmdList->Close();
    Check(g_device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&g_fence)), "CreateFence");
    g_fenceEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
}

static void WaitForFence(UINT64 value)
{
    if (g_fence->GetCompletedValue() < value) {
        g_fence->SetEventOnCompletion(value, g_fenceEvent);
        WaitForSingleObject(g_fenceEvent, INFINITE);
    }
}

static void Transition(ID3D12Resource* resource, D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to)
{
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource = resource;
    b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    b.Transition.StateBefore = from;
    b.Transition.StateAfter = to;
    g_cmdList->ResourceBarrier(1, &b);
}

// ---------------------------------------------------------------- render ----

// Appends one bar per interval between consecutive timestamps, newest on the right.
static void AddIntervalGraph(std::vector<D3D12_RECT>& rects, const std::deque<int64_t>& times,
                             int left, int right, int bottom, int height, double fullScaleMs, int barWidth)
{
    int x = right - barWidth;
    for (size_t i = times.size(); i >= 2 && x >= left; --i, x -= barWidth) {
        double ms = ToSeconds(times[i - 1] - times[i - 2]) * 1000.0;
        int h = std::min(height, int(ms / fullScaleMs * height));
        rects.push_back({x, bottom - h, x + barWidth - 1, bottom});
    }
}

static void AddReferenceLines(std::vector<D3D12_RECT>& rects, std::vector<D3D12_RECT>& labels,
                              int left, int right, int bottom, int height, double fullScaleMs)
{
    for (double hz : {120.0, 60.0, 48.0, 30.0, 20.0}) {
        int y = bottom - int(1000.0 / hz / fullScaleMs * height);
        rects.push_back({left, y, right, y + 1});
        AddText(labels, right + 8, y - 7, 2, std::to_string(int(hz)) + " HZ");
    }
}

static void Render(double animationTime, const Metrics& m)
{
    UINT index = g_swapChain->GetCurrentBackBufferIndex();
    WaitForFence(g_fenceValues[index]);
    g_allocators[index]->Reset();
    g_cmdList->Reset(g_allocators[index].Get(), nullptr);
    Transition(g_backBuffers[index].Get(), D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);

    D3D12_CPU_DESCRIPTOR_HANDLE rtv = g_rtvHeap->GetCPUDescriptorHandleForHeapStart();
    rtv.ptr += SIZE_T(index) * g_rtvStride;

    const float background[4] = {0.06f, 0.06f, 0.08f, 1};
    const float white[4] = {1, 1, 1, 1};
    const float gray[4] = {0.35f, 0.35f, 0.4f, 1};
    const float cyan[4] = {0.2f, 0.8f, 1.0f, 1};
    const float orange[4] = {1.0f, 0.55f, 0.1f, 1};
    const float green[4] = {0.3f, 1.0f, 0.4f, 1};
    g_cmdList->ClearRenderTargetView(rtv, background, 0, nullptr);

    int w = int(g_width), h = int(g_height);

    // Moving bars: judder and tearing are easiest to see on vertical edges.
    std::vector<D3D12_RECT> bars;
    const double speed = 960.0; // px/s
    for (int i = 0; i < 4; ++i) {
        int x = int(std::fmod(animationTime * speed + i * w / 4.0, double(w + 80))) - 80;
        bars.push_back({std::max(0, x), h / 2 - 40, std::min(w, x + 80), h - 40});
    }
    g_cmdList->ClearRenderTargetView(rtv, white, UINT(bars.size()), bars.data());

    // Interval graphs: frame time (cyan) above vblank interval (orange).
    const int graphLeft = 40, graphRight = w - 160, graphHeight = 150;
    const double fullScaleMs = 60.0;
    std::vector<D3D12_RECT> refs, text, frameBars, vblankBars;
    AddReferenceLines(refs, text, graphLeft, graphRight, 440, graphHeight, fullScaleMs);
    AddReferenceLines(refs, text, graphLeft, graphRight, 620, graphHeight, fullScaleMs);
    AddIntervalGraph(frameBars, g_presents, graphLeft, graphRight, 440, graphHeight, fullScaleMs, 5);
    {
        std::lock_guard<std::mutex> lock(g_vblankMutex);
        AddIntervalGraph(vblankBars, g_vblanks, graphLeft, graphRight, 620, graphHeight, fullScaleMs, 5);
    }
    g_cmdList->ClearRenderTargetView(rtv, gray, UINT(refs.size()), refs.data());
    if (!frameBars.empty())
        g_cmdList->ClearRenderTargetView(rtv, cyan, UINT(frameBars.size()), frameBars.data());
    if (!vblankBars.empty())
        g_cmdList->ClearRenderTargetView(rtv, orange, UINT(vblankBars.size()), vblankBars.data());

    char line[160];
    AddText(text, 40, 30, 4, std::string("VRR TEST   PRESENT: ") +
                (g_syncIntervalMode ? "SYNC 1" : (g_tearingSupported ? "SYNC 0 + ALLOW TEARING" : "SYNC 0")));
    snprintf(line, sizeof(line), "TARGET FPS: %.0f%s", g_targetFps, g_sweep ? "  (SWEEP 30-120)" : "");
    AddText(text, 40, 80, 4, line);
    snprintf(line, sizeof(line), "APP FPS: %.1f    DISPLAY VBLANK RATE: %.1f HZ", m.appFps, m.vblankHz);
    AddText(text, 40, 130, 4, line);
    if (m.refreshPerFrame > 0)
        snprintf(line, sizeof(line), "DXGI STATS: %.2f REFRESH/FRAME  %.1f HZ", m.refreshPerFrame, m.statsHz);
    else
        snprintf(line, sizeof(line), "DXGI STATS: UNAVAILABLE");
    AddText(text, 40, 180, 4, line);
    AddText(text, 40, 270, 2, "FRAME TIME");
    AddText(text, 40, 450, 2, "DISPLAY VBLANK INTERVAL");
    AddText(text, 40, h - 30, 2, "UP/DOWN +-1   LEFT/RIGHT +-10   1-7 PRESETS 30/40/48/60/75/90/120   S SWEEP   M PRESENT MODE   ESC QUIT");
    g_cmdList->ClearRenderTargetView(rtv, white, UINT(text.size()), text.data());

    std::vector<D3D12_RECT> verdict;
    AddText(verdict, 40, 228, 4, Verdict(m));
    g_cmdList->ClearRenderTargetView(rtv, green, UINT(verdict.size()), verdict.data());

    Transition(g_backBuffers[index].Get(), D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
    g_cmdList->Close();
    ID3D12CommandList* lists[] = {g_cmdList.Get()};
    g_queue->ExecuteCommandLists(1, lists);

    if (g_syncIntervalMode)
        g_swapChain->Present(1, 0);
    else
        g_swapChain->Present(0, g_tearingSupported ? DXGI_PRESENT_ALLOW_TEARING : 0);

    g_queue->Signal(g_fence.Get(), ++g_fenceCounter);
    g_fenceValues[index] = g_fenceCounter;
}

static void RecordPresent()
{
    g_presents.push_back(Now());
    while (g_presents.size() > 4000)
        g_presents.pop_front();

    DXGI_FRAME_STATISTICS st = {};
    if (SUCCEEDED(g_swapChain->GetFrameStatistics(&st)) && st.SyncQPCTime.QuadPart != 0) {
        if (g_stats.empty() || g_stats.back().presentCount != st.PresentCount) {
            g_stats.push_back({st.PresentCount, st.SyncRefreshCount, st.SyncQPCTime.QuadPart});
            while (g_stats.size() > 4000)
                g_stats.pop_front();
        }
    }
}

// ---------------------------------------------------------------- window ----

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    static const double presets[] = {30, 40, 48, 60, 75, 90, 120};
    switch (msg) {
    case WM_KEYDOWN:
        switch (wp) {
        case VK_ESCAPE: g_quit = true; break;
        case VK_UP: g_targetFps += 1; g_sweep = false; break;
        case VK_DOWN: g_targetFps -= 1; g_sweep = false; break;
        case VK_RIGHT: g_targetFps += 10; g_sweep = false; break;
        case VK_LEFT: g_targetFps -= 10; g_sweep = false; break;
        case 'S': g_sweep = !g_sweep; break;
        case 'M': g_syncIntervalMode = !g_syncIntervalMode; break;
        default:
            if (wp >= '1' && wp <= '7') {
                g_targetFps = presets[wp - '1'];
                g_sweep = false;
            }
        }
        g_targetFps = std::clamp(g_targetFps, 10.0, 240.0);
        return 0;
    case WM_SETCURSOR:
        SetCursor(nullptr);
        return TRUE;
    case WM_CLOSE:
    case WM_DESTROY:
        g_quit = true;
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

static std::wstring LogPath()
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring p(path);
    return p.substr(0, p.find_last_of(L"\\/") + 1) + L"vrrtest-log.txt";
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR cmdLine, int)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    timeBeginPeriod(1);
    LARGE_INTEGER f;
    QueryPerformanceFrequency(&f);
    g_qpcFreq = f.QuadPart;
    g_waitTimer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    const bool autoMode = cmdLine && wcsstr(cmdLine, L"--auto") != nullptr;

    // --vblank N: no window; logs the desktop's real refresh rate every 250 ms
    // for N seconds, to see how the idle DWM desktop behaves under VRR.
    if (const wchar_t* arg = cmdLine ? wcsstr(cmdLine, L"--vblank") : nullptr) {
        int seconds = _wtoi(arg + 8);
        if (seconds <= 0)
            seconds = 10;
        ComPtr<IDXGIFactory1> factory;
        ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))) ||
            FAILED(factory->EnumAdapters1(0, &adapter)) ||
            FAILED(adapter->EnumOutputs(0, &g_output)))
            return 1;
        FILE* vlog = nullptr;
        _wfopen_s(&vlog, LogPath().c_str(), L"w");
        if (!vlog)
            return 1;
        fprintf(vlog, "seconds,vblank_hz\n");
        const int64_t begin = Now();
        int64_t windowStart = begin;
        int count = 0;
        while (ToSeconds(Now() - begin) < seconds) {
            if (FAILED(g_output->WaitForVBlank()))
                break;
            ++count;
            int64_t now = Now();
            if (now - windowStart >= g_qpcFreq / 4) {
                fprintf(vlog, "%.2f,%.1f\n", ToSeconds(now - begin), count / ToSeconds(now - windowStart));
                windowStart = now;
                count = 0;
            }
        }
        fclose(vlog);
        return 0;
    }

    WNDCLASSW wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = instance;
    wc.lpszClassName = L"VrrTest";
    RegisterClassW(&wc);

    MONITORINFO mi = {sizeof(mi)};
    GetMonitorInfoW(MonitorFromPoint({0, 0}, MONITOR_DEFAULTTOPRIMARY), &mi);
    g_width = UINT(mi.rcMonitor.right - mi.rcMonitor.left);
    g_height = UINT(mi.rcMonitor.bottom - mi.rcMonitor.top);
    g_hwnd = CreateWindowExW(0, L"VrrTest", L"VrrTest", WS_POPUP | WS_VISIBLE,
                             mi.rcMonitor.left, mi.rcMonitor.top, int(g_width), int(g_height),
                             nullptr, nullptr, instance, nullptr);
    SetForegroundWindow(g_hwnd);

    InitD3D();
    std::thread vblankThread(VBlankThread);

    // --auto: each step settles for 2 s, then measures over the next 3 s.
    const double autoSteps[] = {120, 100, 90, 75, 60, 50, 48, 45, 40, 35, 30, 28, 25, 22, 20, 18};
    const size_t autoStepCount = sizeof(autoSteps) / sizeof(autoSteps[0]);
    const double settleSeconds = 2.0, measureSeconds = 3.0;
    size_t autoStep = 0;
    FILE* log = nullptr;
    if (autoMode) {
        _wfopen_s(&log, LogPath().c_str(), L"w");
        if (log)
            fprintf(log, "target_fps,app_fps,vblank_hz,dxgi_refresh_per_frame,dxgi_hz,verdict\n");
        g_targetFps = autoSteps[0];
    }

    const int64_t start = Now();
    int64_t stepStart = start;
    int64_t nextFrame = start;
    int64_t lastMetrics = 0;
    Metrics metrics;

    while (!g_quit) {
        MSG msg;
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        if (g_quit)
            break;

        double elapsed = ToSeconds(Now() - start);
        if (g_sweep)
            g_targetFps = 75.0 - 45.0 * std::cos(elapsed * 2.0 * 3.14159265358979 / 12.0);

        if (autoMode) {
            double inStep = ToSeconds(Now() - stepStart);
            if (inStep >= settleSeconds + measureSeconds) {
                Metrics r = ComputeMetrics(measureSeconds);
                if (log) {
                    fprintf(log, "%.0f,%.2f,%.2f,%.3f,%.2f,%s\n", g_targetFps, r.appFps, r.vblankHz,
                            r.refreshPerFrame, r.statsHz, Verdict(r).c_str());
                    fflush(log);
                }
                if (++autoStep == autoStepCount)
                    break;
                g_targetFps = autoSteps[autoStep];
                stepStart = Now();
            }
        }

        WaitForSingleObjectEx(g_latencyWaitable, 1000, TRUE);

        int64_t period = int64_t(g_qpcFreq / g_targetFps);
        nextFrame += period;
        int64_t now = Now();
        if (nextFrame < now - period)
            nextFrame = now; // fell behind (e.g. after a stall): resynchronise
        WaitUntil(nextFrame);

        if (Now() - lastMetrics > g_qpcFreq / 4) {
            metrics = ComputeMetrics(1.0);
            lastMetrics = Now();
        }
        Render(ToSeconds(nextFrame - start), metrics);
        RecordPresent();
    }

    if (log)
        fclose(log);
    g_queue->Signal(g_fence.Get(), ++g_fenceCounter);
    WaitForFence(g_fenceCounter);
    g_vblankRunning = false;
    vblankThread.join();
    timeEndPeriod(1);
    return 0;
}
