using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LuaInterface
{
    public static class LuaRegistrationHelper
    {
        #region Tagged instance methods
        /// <summary>
        /// Registers all public instance methods in an object tagged with <see cref="LuaGlobalAttribute"/> as Lua global functions
        /// </summary>
        /// <param name="LuaInstance">The Lua VM to add the methods to</param>
        /// <param name="Obj">The object to get the methods from</param>
        public static void TaggedInstanceMethods(Lua LuaInstance, object Obj)
        {
            #region Sanity checks
            if (LuaInstance == null) throw new ArgumentNullException("LuaInstance");
            if (Obj == null) throw new ArgumentNullException("Obj");
            #endregion

            foreach (MethodInfo Method in Obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                foreach (LuaGlobalAttribute Attribute in Method.GetCustomAttributes(typeof(LuaGlobalAttribute), true))
                {
                    if (string.IsNullOrEmpty(Attribute.Name))
                    {
                        LuaInstance.RegisterFunction(Method.Name, Obj, Method); // CLR name
                    }
                    else
                    {
                        LuaInstance.RegisterFunction(Attribute.Name, Obj, Method); // Custom name
                    }
                }
            }
        }
        #endregion

        #region Tagged static methods
        /// <summary>
        /// Registers all public static methods in a class tagged with <see cref="LuaGlobalAttribute"/> as Lua global functions
        /// </summary>
        /// <param name="LuaInstance">The Lua VM to add the methods to</param>
        /// <param name="ObjType">The class type to get the methods from</param>
        public static void TaggedStaticMethods(Lua LuaInstance, Type ObjType)
        {
            #region Sanity checks
            if (LuaInstance == null) throw new ArgumentNullException("LuaInstance");
            if (ObjType == null) throw new ArgumentNullException("ObjType");
            if (!ObjType.IsClass) throw new ArgumentException("The type must be a class!", "ObjType");
            #endregion

            foreach (MethodInfo Method in ObjType.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                foreach (LuaGlobalAttribute Attribute in Method.GetCustomAttributes(typeof(LuaGlobalAttribute), false))
                {
                    if (string.IsNullOrEmpty(Attribute.Name))
                    {
                        LuaInstance.RegisterFunction(Method.Name, null, Method); // CLR name
                    }
                    else
                    {
                        LuaInstance.RegisterFunction(Attribute.Name, null, Method); // Custom name
                    }
                }
            }
        }
        #endregion

        #region Enumeration
        /// <summary>
        /// Registers an enumeration's values for usage as a Lua variable table
        /// </summary>
        /// <typeparam name="T">The enum type to register</typeparam>
        /// <param name="LuaInstance">The Lua VM to add the enum to</param>
        [SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter", Justification = "The type parameter is used to select an enum type")]
        public static void Enumeration<T>(Lua LuaInstance)
        {
            #region Sanity checks
            if (LuaInstance == null) throw new ArgumentNullException("LuaInstance");
            #endregion

            Type ObjType = typeof(T);
            if (!ObjType.IsEnum) throw new ArgumentException("The type must be an enumeration!");

            string[] Names = Enum.GetNames(ObjType);
            T[] Values = (T[])Enum.GetValues(ObjType);

            LuaInstance.NewTable(ObjType.Name);
            for (int i = 0; i < Names.Length; i++)
            {
                string Path = ObjType.Name + "." + Names[i];
                LuaInstance[Path] = Values[i];
            }
        }
        #endregion
    }
}
