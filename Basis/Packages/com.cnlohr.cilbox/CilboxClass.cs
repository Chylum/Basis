using Cilbox;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cilbox
{
    public class CilboxClass
    {
        public Cilbox box;
        public String className;

        public object[] staticFields;
        public String[] staticFieldNames;
        public Type[] staticFieldTypes;

        public String[] instanceFieldNames;
        public Type[] instanceFieldTypes;

        public Dictionary<String, uint> methodNameToIndex;
        public Dictionary<String, uint> methodFullSignatureToIndex;

        public CilboxMethod[] methods;

        public uint[] importFunctionToId; // from ImportFunctionID

        public bool LoadCilboxClass(Cilbox box, String className, Serializee classData)
        {
            this.box = box;
            this.className = className;

            Dictionary<String, Serializee> classProps = classData.AsMap();

            LoadStaticFields(classProps);

            LoadInstanceFields(classProps);

            LoadDeserMethods(classProps);

            SetupImportFunctions();

            return true;
        }

        private void LoadStaticFields(Dictionary<string, Serializee> classProps)
        {
            uint id = 0;
            Serializee[] staticFields = classProps["staticFields"].AsArray();
            int sfnum = staticFields.Length;
            this.staticFields = new object[sfnum];
            staticFieldNames = new String[sfnum];
            staticFieldTypes = new Type[sfnum];
            for (int k = 0; k < sfnum; k++)
            {
                Dictionary<String, Serializee> field = staticFields[k].AsMap();
                String fieldName = staticFieldNames[id] = field["name"].AsString();
                Type t = staticFieldTypes[id] = box.usage.GetNativeTypeFromSerializee(field["type"]);

                //staticFieldIDs[id] = Cilbox.FindInternalMetadataID( className, 4, fieldName );
                this.staticFields[id] = CilboxUtil.DeserializeDataForProxyField(t, "");
                id++;
            }
        }

        private void LoadInstanceFields(Dictionary<string, Serializee> classProps)
        {
            Serializee[] instanceFields = classProps["instanceFields"].AsArray();
            int ifnum = instanceFields.Length;
            instanceFieldNames = new String[ifnum];
            instanceFieldTypes = new Type[ifnum];

            uint id = 0;
            for (int k = 0; k < ifnum; k++)
            {
                Dictionary<String, Serializee> field = instanceFields[k].AsMap();
                String fieldName = instanceFieldNames[id] = field["name"].AsString();
                instanceFieldTypes[id] = box.usage.GetNativeTypeFromSerializee(field["type"]);
                id++;
            }
        }

        private void LoadDeserMethods(Dictionary<string, Serializee> classProps)
        {
            uint id = 0;
            Dictionary<String, Serializee> deserMethods = classProps["methods"].AsMap();
            int mnum = deserMethods.Count;
            methods = new CilboxMethod[mnum];
            methodNameToIndex = new Dictionary<String, uint>();
            methodFullSignatureToIndex = new Dictionary<String, uint>();
            foreach (var k in deserMethods)
            {
                methods[id] = new CilboxMethod();
                methods[id].Load(this, k.Key, k.Value);
                methodNameToIndex[(String)k.Key] = id;
                methodFullSignatureToIndex[methods[id].fullSignature] = id;
                id++;
            }
        }

        private void SetupImportFunctions()
        {
            // These imports are for things like Start(), Update(), Awake(), etc...
            // so that we can call back into the class.
            int numImportFunctions = Enum.GetNames(typeof(ImportFunctionID)).Length;
            importFunctionToId = new uint[numImportFunctions];
            for (int i = 0; i < numImportFunctions; i++)
            {
                String fn = Enum.GetName(typeof(ImportFunctionID), i);
                if (i == 0) fn = ".ctor";
                uint idx = 0;
                importFunctionToId[i] = 0xffffffff;
                if (methodNameToIndex.TryGetValue(fn, out idx))
                {
                    importFunctionToId[i] = idx;
                }
            }
        }
    }
}
