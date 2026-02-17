using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Cilbox
{
    public class CilMetadataTokenInfo
    {
        public CilMetadataTokenInfo(MetaTokenType type) { this.type = type; }
        public MetaTokenType type;
        public bool isValid;
        public int fieldIndex; // Only used for fields.
        public bool fieldIsStatic;

        public Type nativeType; // Used for types.
        public bool nativeTypeIsStackType;
        public bool nativeTypeIsCilboxProxy;
        public StackType nativeTypeStackType;

        public byte[] arrayInitializerData;

        // Todo handle interpreted types.
        public bool isNative;
        public MethodBase nativeMethod;
        public int interpretiveMethod; // If nativeToken is 0, then it's a interpreted call.
        public int interpretiveMethodClass; // If nativeToken is 0, then it's a interpreted call class

        // For string, type = 0x70, string is in fields[0] (escaped) and Name, unescaped.
        // For methods, type = 10, Declaring Type is in fields[0], Method is in fields[1], Full name is in fields[2] assembly name is in fields[3]
        // For fields, type = 4, Declaring Type is in fields[0], Name is in fields[1], Type is in fields[2]
        //public String [] fields;

        public String Name;
        public String declaringTypeName;
        //public String ToString() { return Name; }

        public delegate StackElement DelegateOverride(CilMetadataTokenInfo ths, ArraySegment<StackElement> stackBufferIn, ArraySegment<StackElement> parametersIn);
        public object opaque;
        public DelegateOverride shim = null;
        public bool shimIsVoid;
        public bool shimIsStatic;
        public int shimParameterCount;
    }
}
