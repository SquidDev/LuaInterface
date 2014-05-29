using System;
using System.Runtime.Serialization;

namespace LuaInterface
{
    /// <summary>
    /// Exceptions thrown by the Lua runtime
    /// </summary>
    [Serializable]
    public class LuaException : Exception
    {
        public LuaException()
        {}

        public LuaException(string Message) : base(Message)
        {}

        public LuaException(string Message, Exception InnerException) : base(Message, InnerException)
        {}

        protected LuaException(SerializationInfo Info, StreamingContext Context) : base(Info, Context)
        {}
    }
}
