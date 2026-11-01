using System;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>Hosts the engine on its own message-loop thread, as a service or in a console for testing.</summary>
    internal sealed class PowerButtonService : ServiceBase
    {
        public const string Name = "Atlas8PowerButton";

        private Thread _thread;
        private ButtonEngine _engine;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);

        public PowerButtonService()
        {
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
            CanHandlePowerEvent = true;
            AutoLog = false;
        }

        /// <summary>Runs the engine in the foreground for a while, for testing without the service.</summary>
        public static void RunForeground(int seconds)
        {
            var host = new PowerButtonService();
            host.OnStart(null);
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            host.OnStop();
        }

        protected override void OnStart(string[] args)
        {
            _thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                try
                {
                    _engine = new ButtonEngine();
                }
                catch (Exception ex)
                {
                    Log.Write("Service failed to start: " + ex);
                    _ready.Set();
                    return;
                }

                _ready.Set();
                Application.Run();
            }) { Name = "Engine", IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.WaitOne(10000);
            if (_engine == null)
            {
                throw new InvalidOperationException("The engine did not start; see the log.");
            }
        }

        protected override void OnStop()
        {
            Shutdown("stop");
        }

        protected override void OnShutdown()
        {
            // Windows gets its own handling back for the next boot until the service is running again.
            Shutdown("system shutdown");
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus status)
        {
            var engine = _engine;
            if (engine == null)
            {
                return true;
            }

            switch (status)
            {
                case PowerBroadcastStatus.Suspend:
                    engine.Post(engine.OnSuspend);
                    break;
                case PowerBroadcastStatus.ResumeAutomatic:
                case PowerBroadcastStatus.ResumeSuspend:
                case PowerBroadcastStatus.ResumeCritical:
                    engine.Post(() => engine.OnResumed(status.ToString()));
                    break;
            }

            return true;
        }

        private void Shutdown(string reason)
        {
            var engine = _engine;
            if (engine != null)
            {
                engine.Post(() => engine.Stop(reason));
                _thread?.Join(8000);
            }
        }
    }
}
