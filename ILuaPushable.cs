namespace LuaInterface
{
    /// <summary>
    /// Defines a custom way for an object to be pushed to Lua.
    /// </summary>
    public interface ILuaPushable
    {
        void Push(KopiLua.LuaState State, ObjectTranslator Translator);
        void Push(Lua LuaInstance);
    }
}
