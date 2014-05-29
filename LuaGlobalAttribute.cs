using System;

namespace LuaInterface
{
    /// <summary>
    /// Marks a method for global usage in Lua scripts
    /// </summary>
    /// <see cref="LuaRegistrationHelper.TaggedInstanceMethods"/>
    /// <see cref="LuaRegistrationHelper.TaggedStaticMethods"/>
    [AttributeUsage(AttributeTargets.Method)]
    public class LuaGlobalAttribute : Attribute
    {
        private string _Name;
        private string _Description;
        /// <summary>
        /// An alternative name to use for calling the function in Lua - leave empty for CLR name
        /// </summary>
        public string Name { get { return _Name; } set { _Name = value; }}

        /// <summary>
        /// A description of the function
        /// </summary>
        public string Description { get { return _Description; } set { _Description = value; }}
    }
}
