using System;

namespace LuaInterface
{
    /// <summary>
    /// Base class to provide consistent disposal flow across lua objects. Uses code provided by Yves Duhoux and suggestions by Hans Schmeidenbacher and Qingrui Li
    /// </summary>
    public abstract class LuaBase : IDisposable, ILuaPushable
    {
        private bool Disposed;
        protected int Reference;
        protected Lua LuaInstance;

        public LuaBase(int Reference = 0, Lua LuaInstance = null)
        {
            this.Reference = Reference;
            this.LuaInstance = LuaInstance;
        }

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
            if (!Disposed)
            {
                if (disposeManagedResources)
                {
                    if (Reference != 0)
                        LuaInstance.DisposeObject(Reference);
                }
                LuaInstance = null;
                Disposed = true;
            }
        }

        public override bool Equals(object Obj)
        {
            if (Obj is LuaBase)
            {
                LuaBase LuaObj = (LuaBase)Obj;
                return LuaInstance.CompareRef(LuaObj.Reference, Reference);
            }
            else return false;
        }

        public override int GetHashCode()
        {
            return Reference;
        }

        #region ILuaPushable
        public virtual void Push(KopiLua.LuaState State, ObjectTranslator Translator)
        {
            throw new NotImplementedException();
        }

        public void Push(Lua LuaInstance)
        {
            Push(LuaInstance.LuaState, LuaInstance.Translator);
        }

        public void Push()
        {
            Push(LuaInstance);
        }
        #endregion


    }
}
