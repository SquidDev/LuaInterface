using System;

namespace LuaInterface
{
    /// <summary>
    /// Base class to provide consistent disposal flow across lua objects. Uses code provided by Yves Duhoux and suggestions by Hans Schmeidenbacher and Qingrui Li
    /// </summary>
    public abstract class LuaBase : IDisposable, ILuaPushable
    {
        private bool _Disposed;
        protected int _Reference;
        protected Lua _Interpreter;

        ~LuaBase()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposeManagedResources)
        {
            if (!_Disposed)
            {
                if (disposeManagedResources)
                {
                    if (_Reference != 0)
                        _Interpreter.DisposeObject(_Reference);
                }
                _Interpreter = null;
                _Disposed = true;
            }
        }

        public override bool Equals(object Obj)
        {
            if (Obj is LuaBase)
            {
                LuaBase LuaObj = (LuaBase)Obj;
                return _Interpreter.CompareRef(LuaObj._Reference, _Reference);
            }
            else return false;
        }

        public override int GetHashCode()
        {
            return _Reference;
        }

        #region ILuaPushable
        public void Push(KopiLua.LuaState State, ObjectTranslator Translator)
        {
            throw new NotImplementedException();
        }

        public void Push(Lua LuaInstance)
        {
            Push(LuaInstance.LuaState, LuaInstance.Translator);
        }
        #endregion


    }
}
