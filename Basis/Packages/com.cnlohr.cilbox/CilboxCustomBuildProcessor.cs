using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEditor;
#if UNITY_EDITOR
using UnityEditor.Build;
#endif
using UnityEngine;

namespace Cilbox
{
    ///////////////////////////////////////////////////////////////////////////
    //  EXPORTING  ////////////////////////////////////////////////////////////
    ///////////////////////////////////////////////////////////////////////////

#if UNITY_EDITOR

    // Trigger the scene recompile.  Uuuughhhh someone who knows what they're doing need to rewrite
    // this part.  Also, see this discussion: https://discussions.unity.com/t/onprocessscene-sometimes-gets-skipped/943573/7
    //
    // IProcessSceneWithReport - runs before scene is compiled, against the play-mode tree
    // OnPostBuildPlayerScriptDLLs - it runs at the right time, in a blank scene, but that scene is not what is used.
    // IPostprocessBuildWithReport - happens after build is complete, but also dumped into a temporary scene.
    // IPreprocessBuildWithReport - Happens on the main scene, and outputs are preserved
    // BuildPlayerProcessor - same as IPreprocessBuildWithReport

    class CilboxCustomBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, UnityEditor.Build.Reporting.BuildReport report)
        {
            //Debug.Log( "IProcessSceneWithReport" );
            CilboxScenePostprocessor.OnPostprocessScene();
        }
    }

    class CilboxCustomBuildProcessor2 : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            MonoBehaviour[] allBehavioursThatNeedCilboxing = CilboxUtil.GetAllBehavioursThatNeedCilboxing();

            if (allBehavioursThatNeedCilboxing.Length == 0)
                return;

            Debug.Log($"Dirtying scene, found {allBehavioursThatNeedCilboxing.Length} cilboxable elements.");

            // PLEASE LET ME KNOW IF YOU KNOW A BETTER WAY https://discussions.unity.com/t/onprocessscene-sometimes-gets-skipped/943573/6
            GameObject dirtier = GameObject.Find("/CilboxDirtier");
            if (!dirtier)
                dirtier = new GameObject("CilboxDirtier");
            dirtier.hideFlags = HideFlags.HideInHierarchy;
            dirtier.transform.position = new Vector3(UnityEngine.Random.Range(-100, 100), UnityEngine.Random.Range(-100, 100), UnityEngine.Random.Range(-100, 100));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
    public class CilboxScenePostprocessor
    {
        //[PostProcessSceneAttribute (2)] This is actually called by IProcessSceneWithReport
        public static void OnPostprocessScene()
        {

            ProfilerMarker perf = new ProfilerMarker("Initial Setup"); perf.Begin();

            MonoBehaviour[] allBehavioursThatNeedCilboxing = CilboxUtil.GetAllBehavioursThatNeedCilboxing();

            Debug.Log($"Postprocessing scene. Cilbox scripts to do: {allBehavioursThatNeedCilboxing.Length}");
            if (allBehavioursThatNeedCilboxing.Length == 0) return;


            Dictionary<String, Serializee> assemblyMetadata = new Dictionary<String, Serializee>();
            Dictionary<uint, String> originalMetaToFriendlyName = new Dictionary<uint, String>();
            Dictionary<int, uint> assemblyMetadataReverseOriginal = new Dictionary<int, uint>();

            uint mdcount = 1; // token 0 is invalid.
            int bytecodeLength = 0;
            Dictionary<String, Serializee> classes = new Dictionary<String, Serializee>();
            Dictionary<String, Serializee> allClassMethods = new Dictionary<String, Serializee>();

            perf.End(); perf = new ProfilerMarker("Main Getting Types"); perf.Begin();

            // Make sure the cilbox script is in use in the scene.
            HashSet<System.Type> TypesInUseInScene = null;

            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            if (activeScene != null)
            {
                TypesInUseInScene = new HashSet<System.Type>();
                GameObject[] rootObjects = activeScene.GetRootGameObjects();
                foreach (GameObject root in rootObjects)
                {
                    MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);

                    foreach (MonoBehaviour component in components)
                    {
                        if (component != null)
                        {
                            Type t = component.GetType();
                            if (!TypesInUseInScene.Contains(t))
                            {
                                TypesInUseInScene.Add(t);
                            }
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("No scene loaded. Converting ALL Cilboxable scripts.");
            }

            System.Reflection.Assembly[] assys = AppDomain.CurrentDomain.GetAssemblies();
            foreach (System.Reflection.Assembly proxyAssembly in assys)
            {
                foreach (Type type in proxyAssembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(CilboxableAttribute), true).Length <= 0)
                        continue;

                    // Cilbox is not in use... But do ALL cilboxes if no scene is loaded.
                    if (TypesInUseInScene != null && !TypesInUseInScene.Contains(type)) continue;

                    ProfilerMarker perfType = new ProfilerMarker(type.ToString()); perfType.Begin();

                    Dictionary<String, Serializee> methods = new Dictionary<String, Serializee>();

                    int mtyp; // Which round of methods are we getting.
                              // Iterate twice. Once for methods, then for constructors.
                    for (mtyp = 0; mtyp < 2; mtyp++)
                    {
                        MethodBase[] me;
                        if (mtyp == 0)
                            me = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                        else
                            me = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

                        foreach (MethodBase m in me)
                        {
                            if (m.DeclaringType.Assembly != proxyAssembly)
                            {
                                // We can't export things that are part of Unity.
                                continue;
                            }

                            ProfilerMarker perfMethod = new ProfilerMarker(m.ToString()); perfMethod.Begin();

                            String methodName = m.Name;
                            Dictionary<String, Serializee> MethodProps = new Dictionary<String, Serializee>();
                            MethodBody mb = m.GetMethodBody();
                            if (mb == null)
                            {
                                Debug.Log($"NOTE: {m.Name} does not have a body");
                                // Things like MemberwiseClone, etc.
                                perfMethod.End();
                                continue;
                            }

                            byte[] byteCodeIn = mb.GetILAsByteArray();
                            byte[] byteCode = new byte[byteCodeIn.Length];
                            Array.Copy(byteCodeIn, byteCode, byteCodeIn.Length);

                            String sOpcodeStr = ""; int iOpcodeStrI = 0;
                            //if( !ExtractAndTransformMetas( proxyAssembly, ref ba, ref assemblyMetadata, ref assemblyMetadataReverseOriginal, ref mdcount ) ) continue;
                            //static bool ExtractAndTransformMetas( Assembly proxyAssembly, ref byte [] byteCode, ref OrderedDictionary od, ref Dictionary< uint, uint > assemblyMetadataReverseOriginal, ref int mdcount )
                            {
                                int i = 0;
                                i = 0;
                                try
                                {
                                    do
                                    {
                                        int starti = i;
                                        for (; iOpcodeStrI <= starti; iOpcodeStrI++)
                                            sOpcodeStr += ((iOpcodeStrI < starti) ? " " : "*") + byteCode[iOpcodeStrI].ToString("X2");

                                        CilboxUtil.OpCodes.OpCode oc;
                                        try
                                        {
                                            oc = CilboxUtil.OpCodes.ReadOpCode(byteCode, ref i);
                                        }
                                        catch (Exception e)
                                        {
                                            Debug.LogError(e.ToString());
                                            sOpcodeStr += " XXXX ";
                                            for (; iOpcodeStrI < byteCode.Length; iOpcodeStrI++)
                                            {
                                                sOpcodeStr += byteCode[iOpcodeStrI].ToString("X2") + " ";
                                            }
                                            Debug.LogError("Exception decoding opcode at address " + i + " in " + m.Name + "\n" + sOpcodeStr);
                                            throw;
                                        }
                                        int opLen = CilboxUtil.OpCodes.OperandLength[(int)oc.OperandType];
                                        int backupi = i;
                                        uint operand = (uint)CilboxUtil.BytecodePullLiteral(byteCode, ref i, opLen);

                                        bool changeOperand = true;
                                        uint writebackToken = mdcount;

                                        // Check to see if this is a meta that we care about.  Then rewrite in a new identifier.
                                        // ResolveField, ResolveMember, ResolveMethod, ResolveSignature, ResolveString, ResolveType
                                        // We sort of want to let the other end know what they are. So we mark them with the code
                                        // from here: https://github.com/jbevain/cecil/blob/master/Mono.Cecil.Metadata/TableHeap.cs#L16

                                        CilboxUtil.OpCodes.OperandType ot = oc.OperandType;

                                        if (ot == CilboxUtil.OpCodes.OperandType.InlineTok)
                                        {
                                            // Cheating: Just convert it to whatever we think it is.
                                            switch (operand >> 24)
                                            {
                                                case 0x04: // Special case handling for constant initializers.
                                                    if (!assemblyMetadataReverseOriginal.TryGetValue((int)operand, out writebackToken))
                                                    {
                                                        writebackToken = mdcount;
                                                        // Special <PrivateImplementationDetails>+__StaticArrayInitTypeSize=24 instance.
                                                        FieldInfo rf = proxyAssembly.ManifestModule.ResolveField((int)operand);
                                                        // Extract raw bytes from initializer type
                                                        byte[] bytes = new byte[System.Runtime.InteropServices.Marshal.SizeOf(rf.FieldType)];
                                                        GCHandle h = GCHandle.Alloc(rf.GetValue(null), GCHandleType.Pinned);
                                                        Marshal.Copy(h.AddrOfPinnedObject(), bytes, 0, bytes.Length);
                                                        h.Free();
                                                        // Now, encode our array initializer to base64.
                                                        Dictionary<String, Serializee> thisMeta = new Dictionary<String, Serializee>();
                                                        thisMeta["mt"] = new Serializee(((int)MetaTokenType.mtArrayInitializer).ToString());
                                                        thisMeta["data"] = Serializee.CreateFromBlob(bytes);
                                                        originalMetaToFriendlyName[mdcount] = rf.Name;
                                                        assemblyMetadata[(mdcount++).ToString()] = new Serializee(thisMeta);
                                                    }
                                                    break;
                                                /*
                                                    case 0x02: // Inline Token for Type (typically used with typeof())
                                                        if( !assemblyMetadataReverseOriginal.TryGetValue( (int)operand, out writebackToken ) )
                                                        {
                                                            // TODO: Actually investigate this.  See if we really need it.
                                                            writebackToken = mdcount;
                                                            Type ty = proxyAssembly.ManifestModule.ResolveType( (int)operand );
                                                            Dictionary<String, Serializee> fieldProps = new Dictionary<String, Serializee>();
                                                            fieldProps["mt"] = new Serializee( ((int)MetaTokenType.mtType).ToString() );
                                                            fieldProps["dt"] = CilboxUtil.GetSerializeeFromNativeType( ty );
                                                            assemblyMetadata[(mdcount++).ToString()] = new Serializee( fieldProps );
                                                            originalMetaToFriendlyName[writebackToken] = ty.FullName;
                                                        }
                                                        break;
                                                */
                                                default:
                                                    throw new Exception("Exception decoding opcode at address (confusing meta " + operand.ToString("X8") + ") " + i + " in " + m.Name);
                                            }
                                        }
                                        else if (ot == CilboxUtil.OpCodes.OperandType.InlineSwitch)
                                        {
                                            i += (int)operand * 4;
                                            changeOperand = false;
                                        }
                                        else if (ot == CilboxUtil.OpCodes.OperandType.InlineString)
                                        {
                                            if (!assemblyMetadataReverseOriginal.TryGetValue((int)operand, out writebackToken))
                                            {
                                                writebackToken = mdcount;
                                                Dictionary<String, String> thisMeta = new Dictionary<String, String>();
                                                String st = ((int)MetaTokenType.mtString).ToString();
                                                thisMeta["mt"] = st;
                                                thisMeta["s"] = proxyAssembly.ManifestModule.ResolveString((int)operand);
                                                originalMetaToFriendlyName[mdcount] = st;
                                                assemblyMetadata[(mdcount++).ToString()] = new Serializee(thisMeta);
                                            }
                                        }
                                        else if (ot == CilboxUtil.OpCodes.OperandType.InlineMethod)
                                        {
                                            if (!assemblyMetadataReverseOriginal.TryGetValue((int)operand, out writebackToken))
                                            {
                                                writebackToken = mdcount;
                                                MethodBase tmb = proxyAssembly.ManifestModule.ResolveMethod((int)operand);

                                                Dictionary<String, Serializee> methodProps = new Dictionary<String, Serializee>();

                                                // "Generic constructors are not supported in the .NET Framework version 2.0"
                                                if (!tmb.IsConstructor)
                                                {
                                                    Type[] templateArguments = tmb.GetGenericArguments();
                                                    if (templateArguments.Length > 0)
                                                    {
                                                        Serializee[] argtypes = new Serializee[templateArguments.Length];
                                                        for (int a = 0; a < templateArguments.Length; a++)
                                                            argtypes[a] = CilboxUtil.GetSerializeeFromNativeType(templateArguments[a]);
                                                        methodProps["ga"] = new Serializee(argtypes);
                                                    }
                                                }

                                                methodProps["dt"] = CilboxUtil.GetSerializeeFromNativeType(tmb.DeclaringType);
                                                methodProps["name"] = new Serializee(tmb.Name);

                                                System.Reflection.ParameterInfo[] parameterInfos = tmb.GetParameters();
                                                if (parameterInfos.Length > 0)
                                                {
                                                    Serializee[] parametersSer = new Serializee[parameterInfos.Length];
                                                    for (var j = 0; j < parameterInfos.Length; j++)
                                                    {
                                                        Type ty = parameterInfos[j].ParameterType;
                                                        parametersSer[j] = CilboxUtil.GetSerializeeFromNativeType(ty);
                                                    }
                                                    methodProps["parameters"] = new Serializee(parametersSer);
                                                }
                                                methodProps["fullSignature"] = new Serializee(tmb.ToString());
                                                methodProps["isStatic"] = new Serializee(tmb.IsStatic ? "1" : "0");
                                                methodProps["assembly"] = new Serializee(tmb.DeclaringType.Assembly.GetName().Name);
                                                methodProps["mt"] = new Serializee(((int)MetaTokenType.mtMethod).ToString());
                                                originalMetaToFriendlyName[writebackToken] = tmb.DeclaringType.ToString() + "." + tmb.ToString();
                                                assemblyMetadata[(mdcount++).ToString()] = new Serializee(methodProps);
                                            }
                                        }
                                        else if (ot == CilboxUtil.OpCodes.OperandType.InlineField)
                                        {
                                            if (!assemblyMetadataReverseOriginal.TryGetValue((int)operand, out writebackToken))
                                            {
                                                writebackToken = mdcount;
                                                FieldInfo rf = proxyAssembly.ManifestModule.ResolveField((int)operand);

                                                Dictionary<String, Serializee> fieldProps = new Dictionary<String, Serializee>();
                                                fieldProps["mt"] = new Serializee(((int)MetaTokenType.mtField).ToString());
                                                fieldProps["dt"] = CilboxUtil.GetSerializeeFromNativeType(rf.DeclaringType);
                                                fieldProps["name"] = new Serializee(rf.Name);
                                                //fieldProps["fullName"] = rf.FieldType.FullName;
                                                fieldProps["isStatic"] = new Serializee((rf.IsStatic ? 1 : 0).ToString());
                                                originalMetaToFriendlyName[writebackToken] = rf.Name;
                                                assemblyMetadata[(mdcount++).ToString()] = new Serializee(fieldProps);
                                            }
                                        }
                                        else if (ot == CilboxUtil.OpCodes.OperandType.InlineType)
                                        {
                                            if (!assemblyMetadataReverseOriginal.TryGetValue((int)operand, out writebackToken))
                                            {
                                                writebackToken = mdcount;
                                                Type ty = proxyAssembly.ManifestModule.ResolveType((int)operand);

                                                Dictionary<String, Serializee> fieldProps = new Dictionary<String, Serializee>();
                                                fieldProps["mt"] = new Serializee(((int)MetaTokenType.mtType).ToString());
                                                fieldProps["dt"] = CilboxUtil.GetSerializeeFromNativeType(ty);
                                                assemblyMetadata[(mdcount++).ToString()] = new Serializee(fieldProps);
                                                originalMetaToFriendlyName[writebackToken] = ty.FullName;
                                            }
                                        }
                                        else
                                            changeOperand = false;

                                        if (changeOperand)
                                        {
                                            i = backupi;
                                            assemblyMetadataReverseOriginal[(int)operand] = writebackToken;
                                            CilboxUtil.BytecodeReplaceLiteral(ref byteCode, ref i, opLen, writebackToken);
                                        }
                                        if (i >= byteCode.Length) break;
                                    } while (true);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogError(e.ToString());
                                    continue;
                                }
                            }

                            bytecodeLength += byteCode.Length;
                            MethodProps["body"] = Serializee.CreateFromBlob(byteCode);

                            IList<ExceptionHandlingClause> exceptions = mb.ExceptionHandlingClauses;
                            if (exceptions.Count > 0)
                            {
                                Serializee[] excArray = new Serializee[exceptions.Count];
                                for (int k = 0; k < exceptions.Count; k++)
                                {
                                    ExceptionHandlingClause c = exceptions[k];
                                    Dictionary<String, Serializee> exc = new Dictionary<String, Serializee>();
                                    exc["flags"] = new Serializee(((int)c.Flags).ToString());
                                    exc["tryOff"] = new Serializee(c.TryOffset.ToString());
                                    exc["tryLen"] = new Serializee(c.TryLength.ToString());
                                    exc["hOff"] = new Serializee(c.HandlerOffset.ToString());
                                    exc["hLen"] = new Serializee(c.HandlerLength.ToString());

                                    if (c.Flags == ExceptionHandlingClauseOptions.Clause && c.CatchType != null)
                                    {
                                        exc["cType"] = CilboxUtil.GetSerializeeFromNativeType(c.CatchType);
                                    }
                                    excArray[k] = new Serializee(exc);
                                }
                                MethodProps["eh"] = new Serializee(excArray);
                            }

                            Serializee[] localVars = new Serializee[mb.LocalVariables.Count];
                            for (int i = 0; i < mb.LocalVariables.Count; i++)
                            {
                                LocalVariableInfo lvi = mb.LocalVariables[i];
                                Dictionary<String, Serializee> local = new Dictionary<String, Serializee>();
                                local["name"] = new Serializee(lvi.ToString());
                                local["dt"] = CilboxUtil.GetSerializeeFromNativeType(lvi.LocalType);
                                localVars[i] = new Serializee(local);
                            }
                            MethodProps["locals"] = new Serializee(localVars);

                            ParameterInfo[] parameters = m.GetParameters();

                            Serializee[] parameterList = new Serializee[parameters.Length];
                            for (int i = 0; i < parameters.Length; i++)
                            {
                                Dictionary<String, Serializee> tpi = new Dictionary<String, Serializee>();
                                tpi["name"] = new Serializee(parameters[i].Name);
                                tpi["dt"] = CilboxUtil.GetSerializeeFromNativeType(parameters[i].ParameterType);
                                parameterList[i] = new Serializee(tpi);
                            }
                            MethodProps["parameters"] = new Serializee(parameterList);
                            MethodProps["maxStack"] = new Serializee(mb.MaxStackSize.ToString());
                            MethodProps["isVoid"] = new Serializee((m is MethodInfo) ? (((MethodInfo)m).ReturnType == typeof(void) ? "1" : "0") : "0");
                            MethodProps["isStatic"] = new Serializee(m.IsStatic ? "1" : "0");
                            MethodProps["fullSignature"] = new Serializee(m.ToString());

                            methods[methodName] = new Serializee(MethodProps);
                            perfMethod.End();
                        }
                    }

                    allClassMethods[type.FullName] = new Serializee(methods);
                    perfType.End();
                }

                perf.End(); perf = new ProfilerMarker("Secondary Getting Types"); perf.Begin();

                // Now that we've iterated through all classes, and collected all possible uses of field IDs,
                // go through the classes again, collecting the fields themselves.

                foreach (Type type in proxyAssembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(CilboxableAttribute), true).Length <= 0)
                        continue;

                    ProfilerMarker perfType = new ProfilerMarker(type.ToString()); perfType.Begin();

                    Dictionary<String, Serializee> classProps = new Dictionary<String, Serializee>();

                    // This portion extracts the index information from the current type, and
                    // Writes it back in where it was needed above in the Method call.
                    //
                    for (int lst = 0; lst < 2; lst++)
                    {
                        List<Serializee> fields = new List<Serializee>();
                        int sfid = 0;
                        FieldInfo[] fi;
                        if (lst == 0)
                            fi = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                        else
                            fi = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        foreach (var f in fi)
                        {
                            Dictionary<String, Serializee> dictField = new Dictionary<String, Serializee>();
                            dictField["name"] = new Serializee(f.Name);
                            dictField["type"] = CilboxUtil.GetSerializeeFromNativeType(f.FieldType);
                            fields.Add(new Serializee(dictField));

                            // Fill in our metadata with a class-specific field ID, if this field ID was used in code anywhere.
                            uint mdid;
                            if (assemblyMetadataReverseOriginal.TryGetValue(f.MetadataToken, out mdid))
                            {
                                Serializee sOpen = assemblyMetadata[mdid.ToString()];
                                Dictionary<String, Serializee> m = sOpen.AsMap();
                                m["index"] = new Serializee(sfid.ToString());
                                assemblyMetadata[mdid.ToString()] = new Serializee(m);
                            }
                            sfid++;
                        }
                        classProps[(lst == 0) ? "staticFields" : "instanceFields"] = new Serializee(fields.ToArray());
                    }

                    classProps["methods"] = allClassMethods[type.FullName];
                    classes[type.FullName] = new Serializee(classProps);
                    perfType.End();
                }
            }

            perf.End(); perf = new ProfilerMarker("Assembling"); perf.Begin();

            Dictionary<String, Serializee> assemblyRoot = new Dictionary<String, Serializee>();
            assemblyRoot["classes"] = new Serializee(classes);
            assemblyRoot["metadata"] = new Serializee(assemblyMetadata);
            Serializee assemblySerializee = new Serializee(assemblyRoot);

            perf.End(); perf = new ProfilerMarker("Serializing"); perf.Begin();

            String sAllAssemblyData = Convert.ToBase64String(assemblySerializee.DumpAsMemory().ToArray());

            perf.End(); perf = new ProfilerMarker("Checking If Assembly Changed"); perf.Begin();

            Cilbox[] se = Resources.FindObjectsOfTypeAll(typeof(Cilbox)) as Cilbox[];
            Cilbox tac;
            if (se.Length != 0)
            {
                tac = se[0];
                if (tac.assemblyData != sAllAssemblyData) EditorUtility.SetDirty(tac);
            }
            else
            {
                throw new Exception("You must have an object with Cilbox (Scene or Avatar)");
                //GameObject cilboxDataObject = new GameObject("CilboxData " + new System.Random().Next(0,10000000));
                //tac = cilboxDataObject.AddComponent( typeof(Cilbox) ) as Cilbox;
                //EditorUtility.SetDirty( tac );
            }

            perf.End(); perf = new ProfilerMarker("Applying Assembly"); perf.Begin();

            if (tac.exportDebuggingData)
            {
                GameObject gameObjectAsm = new GameObject("CilboxAsm " + new System.Random().Next(0, 10000000));
                Cilbox b = gameObjectAsm.AddComponent(tac.GetType()) as Cilbox;
                new Task(() => {
                    CilboxUtil.AssemblyLoggerTask(Application.dataPath + "/CilboxLog.txt", sAllAssemblyData, b);
                    UnityEngine.Events.UnityAction deleter = null;
                    deleter = () => { GameObject.Destroy(gameObjectAsm); Application.onBeforeRender -= deleter; };
                    Application.onBeforeRender += deleter;
                }).Start();
            }

            {
                MonoScript ms = MonoScript.FromMonoBehaviour(tac);
                String scriptPath = AssetDatabase.GetAssetPath(ms);
                if (scriptPath == null) Debug.LogError("Can't find path to cilbox for writing XML.");
                else
                {
                    FileInfo fi = new FileInfo(scriptPath);
                    String thisPath = fi.Directory.ToString();
                    new Task(() => {
                        // Tricky bits...
                        //abstract public HashSet<String> GetWhiteListTypes();

                        HashSet<String> allWhiteList = new HashSet<String>();

                        System.Reflection.Assembly[] assys = AppDomain.CurrentDomain.GetAssemblies();
                        foreach (System.Reflection.Assembly proxyAssembly in assys)
                        {
                            foreach (Type type in proxyAssembly.GetTypes())
                            {
                                if (type.GetCustomAttributes(typeof(CilboxTarget), true).Length <= 0)
                                    continue;
                                //HashSet<String> toAdd = (HashSet<String>)type.InvokeMember( "GetWhiteListTypes", BindingFlags.Static | BindingFlags.Public, null, null, null );
                                MethodInfo mi = type.GetMethod("GetWhiteListTypes");
                                HashSet<String> toAdd = (HashSet<String>)mi.Invoke(null, null);
                                allWhiteList.UnionWith(toAdd);
                            }
                        }

                        Dictionary<String, HashSet<String>> fullWhiteList = new Dictionary<String, HashSet<String>>();

                        foreach (String s in allWhiteList)
                        {
                            //System.Reflection.Assembly [] assys = AppDomain.CurrentDomain.GetAssemblies();
                            foreach (System.Reflection.Assembly a in assys)
                            {
                                Type typ = a.GetType(s);
                                if (typ == null) continue;
                                AssemblyName assemName = a.GetName();
                                HashSet<String> hs;
                                if (!fullWhiteList.TryGetValue(assemName.Name, out hs))
                                    hs = fullWhiteList[assemName.Name] = new HashSet<String>();

                                fullWhiteList[assemName.Name].Add(typ.ToString());
                                break;
                            }
                        }

                        StreamWriter CLog = File.CreateText(thisPath + "/link.xml");
                        CLog.WriteLine("<linker>");
                        foreach (var v in fullWhiteList)
                        {
                            CLog.WriteLine($"\t<assembly fullname=\"{v.Key}\">");
                            foreach (String s in v.Value)
                            {
                                CLog.WriteLine($"\t\t<type fullname=\"{s}\" preserve=\"all\"/>");
                            }
                            CLog.WriteLine("\t</assembly>");
                        }
                        CLog.WriteLine("</linker>");
                        CLog.Close();

                    }).Start();
                }
            }

            if (bytecodeLength == 0)
            {
                // This happens the second time around.
            }
            else
            {
                tac.assemblyData = sAllAssemblyData;
                tac.ForceReinit();
            }

            Dictionary<MonoBehaviour, CilboxProxy> refToProxyMap = new Dictionary<MonoBehaviour, CilboxProxy>();
            List<MonoBehaviour> refProxiesOrig = new List<MonoBehaviour>();
            List<CilboxProxy> refProxies = new List<CilboxProxy>();

            perf.End(); perf = new ProfilerMarker("Updating Game Objects"); perf.Begin();

            // Iterate over all GameObjects, and find the ones that have Cilboxable scripts.
            object[] obj = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (object o in obj)
            {
                GameObject g = (GameObject)o;
                MonoBehaviour[] scripts = g.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour m in scripts)
                {
                    // Skip null objects.
                    if (m == null)
                        continue;
                    object[] attribs = m.GetType().GetCustomAttributes(typeof(CilboxableAttribute), true);
                    // Not a proxiable script.
                    if (attribs == null || attribs.Length <= 0)
                        continue;

                    CilboxProxy p = g.AddComponent<CilboxProxy>();
                    refProxies.Add(p);
                    refProxiesOrig.Add(m);
                    refToProxyMap[m] = p;
                }
            }
            perf.End(); perf = new ProfilerMarker("Setting Up Proxies"); perf.Begin();

            var cnt = refProxies.Count;
            for (var i = 0; i < cnt; i++)
            {
                CilboxProxy p = refProxies[i];
                MonoBehaviour m = refProxiesOrig[i];

                p.SetupProxy(tac, m, refToProxyMap);
            }

            perf.End(); perf = new ProfilerMarker("Destroying Silboxable Scripts"); perf.Begin();
            // re-attach the refrences to
            foreach (MonoBehaviour m in allBehavioursThatNeedCilboxing)
            {
                UnityEngine.Object.DestroyImmediate(m);
            }
            perf.End();
        }
    }
#endif

    public enum ImportFunctionID
    {
        dotCtor, // Must be at index 0.
        FixedUpdate,
        Update,
        Start,
        Awake,
        OnTriggerEnter,
        OnTriggerExit,
    }
}
