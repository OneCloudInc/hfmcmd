using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;

using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;

using Command;
using CommandLine;


namespace HFMCmd
{
    /// <summary>
    /// Enumeration of interrupt events that might be received by the process
    /// via a ConsoleCtrlHandler callback.
    /// </summary>
    public enum EInterruptTypes
    {
        Ctrl_C = 0,
        Ctrl_Break = 1,
        Close = 2,
        Logoff = 5,
        Shutdown = 6
    }


    /// <summary>
    /// Class to hold definition of external SetConsoleCtrlHandler routine.
    /// </summary>
    class Win32
    {
        public delegate bool Handler(EInterruptTypes ctrlType);

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(Handler handler, bool Add);
    }



    /// <summary>
    /// Main class used to launch the application.
    /// </summary>
    public class Launcher
    {
        // Reference to class logger
        protected static readonly ILog _log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


        /// <summary>
        /// Static public flag indicating that the application is to terminate
        /// immediately, e.g. in response to a Ctrl-C or Logoff event. Any long-
        /// running command should check this flag periodically and attempt to
        /// abort gracefully.
        /// </summary>
        public static bool Interrupted = false;



        /// <summary>
        /// Main program entry point
        /// </summary>
        public static void Main()
        {
            // Hook up CtrlHandler to handle breaks, logoffs, etc
            Win32.Handler hr = new Win32.Handler(CtrlHandler);
            Win32.SetConsoleCtrlHandler(hr, true);

            // Run the application
            new Application().Run();

            // This line needs to appear at the end of the prgram code as a marker to
            // the GC so that it does not collect our control-key handler
            GC.KeepAlive(hr);
        }


        /// <summary>
        /// Handler to receive control events, such as Ctrl-C and logoff and
        /// shutdown events. As a minimum, this logs the event, so that a record
        /// of why the process exited is maintained.
        /// </summary>
        /// <param name="ctrlType">The type of event that occurred.</param>
        /// <returns>True, indicating we have handled the event.</returns>
        static bool CtrlHandler(EInterruptTypes ctrlType)
        {
            _log.Warn("An interrupt [" + ctrlType + "] has been received");
            Interrupted = true;

            return true;
        }

    }


    public class Application
    {
        // Reference to class logger
        protected static readonly ILog _log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private Registry _commands;
        private Context _context;
        private UI _cmdLine;


        /// <summary>
        /// Main program entry point
        /// </summary>
        public void Run()
        {
            ConfigureLogging();

            // Register commands
            _commands = Registry.FindCommands("HFMCmd");
            _commands = Registry.FindCommands("HFM", _commands);

            // TODO: Process command-line arguments
            _cmdLine = new UI(HFMCmd.Resource.Help.Purpose);
            var arg = _cmdLine.AddPositionalArgument("CommandOrFile",
                    "The name of the command to execute, or the path to a file containing commands to execute");
            arg.Validate = ValidateCommand;


            // Standard command-line arguments
            _cmdLine.AddFlagArgument("Debug", "Enable debug logging");
            var args = _cmdLine.Parse(Environment.GetCommandLineArgs());

            if(args != null) {
                _context = new Context(_commands);
                _context.Set(this);
                //_context.Set(new HFM.Client());
                _context.Invoke(args["CommandOrFile"] as string, args);
            }
        }


        /// <summary>
        /// Configures logging.
        /// </summary>
        protected void ConfigureLogging()
        {
            // Create a console logger
            ConsoleAppender ca = new ConsoleAppender();
            ca.Layout = new log4net.Layout.PatternLayout(
                "%date{HH:mm:ss} %-5level  %message%newline");
            ca.ActivateOptions();
            log4net.Config.BasicConfigurator.Configure(ca);

            Hierarchy logHier = (Hierarchy)LogManager.GetRepository();

            // TODO: Configure exception renderers

            // Set log level
            logHier.Root.Level = log4net.Core.Level.Debug;
        }


        protected bool ValidateCommand(string argVal, out string errorMsg)
        {
            bool ok;
            errorMsg = String.Format("Command file '{0}' not found", argVal);
            if(_commands.Contains(argVal)) {
                ok = true;

                // TODO: Add command arguments as keyword args
                _log.DebugFormat("Adding keyword args for {0} command", argVal);
                Command.Command cmd = _commands[argVal];

                if(cmd.Namespace == "HFM") {
                    // Add standard arguments for logging in
                    _cmdLine.AddKeywordArgument("Domain", "The domain to which the user should be validated in");
                    _cmdLine.AddKeywordArgument("UserName", "The user id to use to connect to HFM");
                    _cmdLine.AddKeywordArgument("Password", "The password to use to connect to HFM");
                    _cmdLine.AddKeywordArgument("Host", "The HFM cluster or server to connect to");
                }

                // Add additional arguments needed by the command
                foreach(var param in cmd.Parameters) {
                    _log.DebugFormat("Adding keyword arg {0}", param.Name);
                    _cmdLine.AddKeywordArgument(param.Name, "");
                }
            }
            else {
                ok = File.Exists(argVal);
            }
            return ok;
        }



        [Command]
        public void Log([DefaultValue("INFO")] string level,
                        [DefaultValue(null)]   string logFile)
        {
            log4net.Repository.ILoggerRepository repo = LogManager.GetRepository();
            Hierarchy logHier = (Hierarchy)repo;
            logHier.Root.Level = repo.LevelMap[level];
        }


    }

}
