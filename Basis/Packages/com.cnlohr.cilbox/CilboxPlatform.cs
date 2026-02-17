using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

namespace Cilbox
{
    // Be warned that this class is totally available to the inner box.
    public class CilboxPlatform
    {
        // This is called only when creating a new action, not when it's called.
        // T is the delegate, not the arguments of the delegate.
        static public object ProxyForGeneratingActions<T>(CilboxProxy proxy, CilboxMethod method)
        {
            CilboxPlatform.DelegateRepackage rp = new CilboxPlatform.DelegateRepackage();
            rp.meth = method;
            rp.o = proxy;

            Type[] parameterTypes = typeof(T).GenericTypeArguments;
            int parameterCount = parameterTypes.Length;

            MethodInfo dMethod = typeof(T).GetMethod("Invoke");
            if (dMethod != null)
            {
                // For some reason, in some contexts we get non-generic delegates.
                // If that's the case, just use the parameters.
                System.Reflection.ParameterInfo[] parameters = dMethod.GetParameters();
                int methodParameters = parameters.Length;
                if (methodParameters > parameterCount)
                {
                    parameterCount = methodParameters;
                    parameterTypes = new Type[parameterCount];
                    for (int n = 0; n < parameterCount; n++)
                        parameterTypes[n] = parameters[n].ParameterType;
                }
            }

            MethodInfo mthis = typeof(CilboxPlatform.DelegateRepackage)
                .GetMethod("ActionCallback" + parameterCount.ToString());
            if (mthis.IsGenericMethod)
                mthis = mthis.MakeGenericMethod(parameterTypes);
            return Delegate.CreateDelegate(typeof(T), rp, mthis);
        }

        public class DelegateRepackage
        {
            public CilboxMethod meth;
            public CilboxProxy o;
            public void ActionCallback0() { meth.Interpret(o, new object[0]); }
            public void ActionCallback1<T0>(T0 o0) { meth.Interpret(o, new object[] { o0 }); }
            public void ActionCallback2<T0, T1>(T0 o0, T1 o1) { meth.Interpret(o, new object[] { o0, o1 }); }
            public void ActionCallback3<T0, T1, T2>(T0 o0, T1 o1, T2 o2) { meth.Interpret(o, new object[] { o0, o1, o2 }); }
            public void ActionCallback4<T0, T1, T2, T3>(T0 o0, T1 o1, T2 o2, T3 o3) { meth.Interpret(o, new object[] { o0, o1, o2, o3 }); }
        }
    }
}
