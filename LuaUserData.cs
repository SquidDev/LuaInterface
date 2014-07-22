using System;
namespace LuaInterface
{
    public class LuaUserData : LuaBase
    {

        public LuaUserData(int reference, Lua interpreter)
        {
            Reference = reference;
            LuaInstance = interpreter;
        }

        /// <summary>
        /// Indexer for string fields of the userdata
        /// </summary>
        public object this[string Field]
        {
            get
            {
                return LuaInstance.GetObject(Reference, Field);
            }
            set
            {
                LuaInstance.SetObject(Reference, Field, value);
            }
        }
        
        /// <summary>
        /// Indexer for numeric fields of the userdata
        /// </summary>
        public object this[object Field]
        {
            get
            {
                return LuaInstance.GetObject(Reference, Field);
            }
            set
            {
                LuaInstance.SetObject(Reference, Field, value);
            }
        }
        
        /// <summary>
        /// Calls the userdata and returns its return values inside an array
        /// </summary>
        public object[] Call(params object[] Args)
        {
            return LuaInstance.CallFunction(this, Args);
        }
        
        /// <summary>
        /// Pushes the userdata into the Lua stack
        /// </summary>
        internal void Push(KopiLua.LuaState LuaState)
        {
            LuaCore.LuaGetRef(LuaState, Reference);
        }

        public override string ToString()
        {
            return String.Format("userdata ({0})", GetHashCode());
        }
    }
}
