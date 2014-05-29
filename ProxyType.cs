using System;
using System.Globalization;
using System.Reflection;

namespace LuaInterface
{
    /// <summary>
    /// Summary description for ProxyType.
    /// </summary>
    public class ProxyType : IReflect
    {

        Type Proxy;

        public ProxyType(Type proxy)
        {
            this.Proxy = proxy;
        }

        /// <summary>
        /// Provide human readable short hand for this proxy object
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "ProxyType(" + UnderlyingSystemType + ")";
        }


        public Type UnderlyingSystemType
        {
            get
            {
                return Proxy;
            }
        }

        public FieldInfo GetField(string name, BindingFlags bindingAttr)
        {
            return Proxy.GetField(name, bindingAttr);
        }

        public FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            return Proxy.GetFields(bindingAttr);
        }

        public MemberInfo[] GetMember(string name, BindingFlags bindingAttr)
        {
            return Proxy.GetMember(name, bindingAttr);
        }

        public MemberInfo[] GetMembers(BindingFlags bindingAttr)
        {
            return Proxy.GetMembers(bindingAttr);
        }

        public MethodInfo GetMethod(string name, BindingFlags bindingAttr)
        {
            return Proxy.GetMethod(name, bindingAttr);
        }

        public MethodInfo GetMethod(string name, BindingFlags bindingAttr, Binder binder, Type[] types, ParameterModifier[] modifiers)
        {
            return Proxy.GetMethod(name, bindingAttr, binder, types, modifiers);
        }

        public MethodInfo[] GetMethods(BindingFlags bindingAttr)
        {
            return Proxy.GetMethods(bindingAttr);
        }

        public PropertyInfo GetProperty(string name, BindingFlags bindingAttr)
        {
            return Proxy.GetProperty(name, bindingAttr);
        }

        public PropertyInfo GetProperty(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers)
        {
            return Proxy.GetProperty(name, bindingAttr, binder, returnType, types, modifiers);
        }

        public PropertyInfo[] GetProperties(BindingFlags bindingAttr)
        {
            return Proxy.GetProperties(bindingAttr);
        }

        public object InvokeMember(string name,	BindingFlags invokeAttr, Binder binder,	object target, object[] args, ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters)
        {
            return Proxy.InvokeMember(name, invokeAttr, binder, target, args, modifiers, culture, namedParameters);
        }

    }
}
