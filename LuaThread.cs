using System.Diagnostics;
namespace LuaInterface
{
    /// <summary>
    /// A 'virtual' instance of the base Lua class
    /// </summary>
    public class LuaVirtualInstance : Lua
    {
        public LuaVirtualInstance(KopiLua.LuaState LuaState, ObjectTranslator Translator)
        {
            this.LuaState = LuaState;
            this.Translator = Translator;
        }
    }
    /// <summary>
    /// An instance for managing Lua threads
    /// </summary>
    public class LuaThread : LuaVirtualInstance
    {
        public LuaThread(KopiLua.LuaState LuaState, ObjectTranslator Translator) : base(LuaState, Translator) { }

        public LuaThreadResume Resume(int NArgs, int OldTop)
        {
            return new LuaThreadResume(LuaCore.LuaResume(LuaState, NArgs), Translator.PopValues(LuaState, OldTop));
        }

        public LuaThreadResume Resume(int NArgs = 0)
        {
            return Resume(NArgs, LuaCore.LuaGetTop(LuaState));
        }

        public LuaThreadResume ResumeWithArgs(params object[] Args)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            foreach (object Arg in Args)
            {
                Push(Arg);
            }

            return Resume(Args.Length, OldTop);
        }

        public LuaThreadResume DoThreadedString(string Chunk)
        {
            int OldTop = LuaCore.LuaGetTop(LuaState);
            LuaCore.LuaLLoadBuffer(LuaState, Chunk, Chunk.Length, "chunk");
            return Resume(0, OldTop);
        }
    }

    public class LuaThreadResume
    {
        public readonly object[] Values;
        public readonly LuaThreadStatus Status;

        public LuaThreadResume(LuaThreadStatus Status, object[] Values = null)
        {
            this.Status = Status;
            this.Values = Values;

            if (Status != LuaThreadStatus.LUA_YIELD && Status != LuaThreadStatus.LUA_DEAD)
            {
                Debug.WriteLine("Error in Lua {0}", Status);
                string Out = "";
                foreach (object O in Values)
                {
                    if (O == null)
                    {
                        Out += "[null], ";
                    }
                    else
                    {
                        Out += "'" + O.ToString() + "', ";
                    }
                }
                Debug.WriteLine(Out);
            }

        }
    }
}
