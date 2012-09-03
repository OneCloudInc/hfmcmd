using System;
using System.Reflection;
using System.Collections.Generic;

using log4net;


namespace Command
{

    /// Define an attribute which will be used to tag methods that can be
    /// invoked from a script or command file.
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    class CommandAttribute : Attribute
    {
    }


    /// Define an attribute which will be used to specify default values for
    /// optional parameters on a Command.
    /// A DefaultValue attribute is used instead of default values on the
    /// actual method, since a) default values are only available from v4 of
    /// .Net, and b) they have restrictions on where they can appear (e.g. only
    /// at the end of the list of parameters).
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    class DefaultValueAttribute : Attribute
    {
        public object Value;

        public DefaultValueAttribute(object val)
        {
            this.Value = val;
        }
    }


    /// Define an attribute which will be used to tag methods that can be
    /// invoked from a script or command file.
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    class SensitiveValueAttribute : Attribute
    {
    }



    /// Records details of a parameter to a Command.
    public class CommandParameter
    {
        public string Name;
        public Type ParameterType;
        public bool HasDefaultValue;
        public object DefaultValue;

        public CommandParameter(ParameterInfo pi)
        {
            this.Name = pi.Name;
            this.ParameterType = pi.ParameterType;
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", this.Name, this.ParameterType.Name);
        }
    }



    /// Represents a method that can be invoked by some external means.
    public class Command
    {
        // Reference to class logger
        protected static readonly ILog _log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Type Type { get; set; }
        public MethodInfo MethodInfo { get; set; }

        public string Name;
        public List<CommandParameter> Parameters = new List<CommandParameter>();


        // Constructor
        public Command(Type t, MethodInfo mi)
        {
            this.Type = t;
            this.MethodInfo = mi;
            this.Name = mi.Name;

            _log.DebugFormat("Found command {0}", this.Name);

            foreach(var pi in mi.GetParameters()) {
                var param = new CommandParameter(pi);
                _log.DebugFormat("Found parameter {0}", param);
                foreach(var attr in pi.GetCustomAttributes(typeof(DefaultValueAttribute), false)) {
                    param.DefaultValue = (attr as DefaultValueAttribute).Value;
                    param.HasDefaultValue = true;
                }
                this.Parameters.Add(param);
            }
        }
    }



    /// Provides a registry of discovered Commands, as well as methods for
    /// discovering them.
    public class Registry
    {
        // Reference to class logger
        protected static readonly ILog _log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Dictionary of command instances keyed by command name
        private IDictionary<string, Command> _commands;

        public Command this[string name] {
            get {
                return _commands[name];
            }
        }

        /// Registers commands (i.e. methods tagged with the Command attribute)
        /// in the current assembly.
        public static Registry FindCommands(string ns)
        {
            return FindCommands(Assembly.GetExecutingAssembly(), ns, null);
        }


        /// Registers commands from the supplied assembly. Commands methods must
        /// be tagged with the attribute Command to be locatable.
        public static Registry FindCommands(Assembly asm, string ns, Registry registry)
        {
            if(registry == null) {
                registry  = new Registry();
            }

            _log.DebugFormat("Searching for commands under namespace '{0}'...", ns);
            foreach(var t in asm.GetExportedTypes()) {
                if(t.IsClass && t.Namespace == ns) {
                    foreach(var mi in t.GetMethods()) {
                        foreach(var attr in mi.GetCustomAttributes(typeof(CommandAttribute), false)) {
                            Command cmd = new Command(t, mi);
                            registry.Add(cmd);
                        }
                    }
                }
            }

            return registry;
        }


        public Registry()
        {
            _commands = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        }


        public void Add(Command cmd)
        {
            _commands.Add(cmd.Name, cmd);
        }

        public bool Contains(string key)
        {
            return _commands.ContainsKey(key);
        }
    }



    /// Records the current context within which Commands are executed. A
    /// Context is like a session object; it holds the current context within
    /// which a Command will be executed, and the method to which a Command
    /// relates will be executed on the object instance of the Command's Type
    /// which is currently in the Context.
    public class Context
    {
        // Reference to class logger
        protected static readonly ILog _log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Registry of known Commands
        protected Registry _registry;

        // A map holding object instances keyed by Type, representing the current
        // context. When a command is invoked, it is invoked on the object that
        // is in the map for the Type on which the Command was registered.
        protected Dictionary<Type, object> _context;


        // Constructor
        public Context(Registry registry)
        {
            _registry = registry;
            _context = new Dictionary<Type, object>();
        }


        public void Set(object val) {
            if(val != null) {
                _context[val.GetType()] = val;
            }
        }


        /// Invoke an instance of the named command, using the supplied arguments
        /// Dictionary to obtain parameter values.
        public object Invoke(string command, Dictionary<string, object> args)
        {
            Command cmd = _registry[command];
            object ctxt = _context[cmd.Type];

            // Create an array of parameters in the order expected
            var parms = new object[cmd.Parameters.Count];
            var i = 0;
            foreach(var param in cmd.Parameters) {
                _log.DebugFormat("Setting parameter {0}", param.Name);
                if(args.ContainsKey(param.Name)) {
                    parms[i++] = args[param.Name];
                }
                else if(param.HasDefaultValue) {
                    // TODO: Deal with missing arg values, default values, etc
                    _log.DebugFormat("No value supplied for argument {0}; using default value", param.Name);
                    parms[i++] = param.DefaultValue;
                }
                else {
                    throw new ArgumentNullException(param.Name,
                            String.Format("No value was specified for a required argument to command '{0}'", cmd.Name));
                }
            }

            var result = cmd.MethodInfo.Invoke(ctxt, parms);

            // If the method returns an object, set it in the context
            if(result != null) {
                this.Set(result);
            }

            return result;
        }
    }

}
