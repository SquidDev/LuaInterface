using System;

namespace LuaInterface
{
    /// <summary>
    /// Exceptions thrown by the Lua runtime because of errors in the script
    /// </summary>
    public class LuaScriptException : LuaException
    {
        private bool IsNet;
        /// <summary>
        /// Returns true if the exception has occured as the result of a .NET exception in user code
        /// </summary>
        public bool IsNetException {
            get { return IsNet; }
            set { IsNet = value;  }
        }

        private readonly string _Source;

        /// <summary>
        /// The position in the script where the exception was triggered.
        /// </summary>
        public override string Source { get { return _Source; } }

        /// <summary>
        /// Creates a new Lua-only exception.
        /// </summary>
        /// <param name="Message">The message that describes the error.</param>
        /// <param name="Source">The position in the script where the exception was triggered.</param>
        public LuaScriptException(string Message, string Source) : base(Message)
        {
            this._Source = Source;
        }

        /// <summary>
        /// Creates a new .NET wrapping exception.
        /// </summary>
        /// <param name="InnerException">The .NET exception triggered by user-code.</param>
        /// <param name="Source">The position in the script where the exception was triggered.</param>
        public LuaScriptException(Exception InnerException, string Source)
            : base(InnerException.Message, InnerException)
        {
            this._Source = Source;
            this.IsNetException = true;
        }

        public override string ToString()
        {
           // Prepend the error source		
            return GetType().FullName + ": " + _Source + Message;
        }
    }
}
