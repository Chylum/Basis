//#define PER_INSTRUCTION_PROFILING

using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections.Specialized;
using System.Collections;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading; // At runtime, only used for a lock (Monitor)

#if UNITY_EDITOR
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using System.IO;
using System.Threading.Tasks;
#endif

namespace Cilbox
{
	abstract public class Cilbox : MonoBehaviour
	{
		public Dictionary< String, int > classes;
		public CilboxClass [] classesList;
		public CilMetadataTokenInfo [] metadatas;
		public String assemblyData;
		private bool initialized = false;

		public static readonly int defaultStackSize = 1024;

		public bool showFunctionProfiling;
		public bool exportDebuggingData;
		public CilboxUsage usage;

		public String disabledReason = "";
		public bool disabled = false;

		public long timeoutLengthUs = 500000; // 500ms Can be changed by specific Cilbox application.
		[HideInInspector] public uint interpreterAccountingDepth = 0;
		[HideInInspector] public long interpreterAccountingDropDead = 0;
		[HideInInspector] public long interpreterAccountingCumulitiveTicks = 0;
		[HideInInspector] public long interpreterInstructionsCount = 0;
		[HideInInspector] public long interpreterTicksInUs = System.Diagnostics.Stopwatch.Frequency / 1000000;

		public long usSpentLastFrame = 0;

		public Cilbox()
		{
			initialized = false;
			usage = new CilboxUsage( this );
		}

		abstract public bool CheckMethodAllowed(  out MethodInfo mi, Type declaringType, String name, Serializee [] parametersIn, Serializee [] genericArgumentsIn, String fullSignature );
		abstract public bool CheckTypeAllowed( String sType );

		public void ForceReinit()
		{
			initialized = false;
		}

		public void BoxInitialize( bool bSimulate = false )
		{
#if UNITY_EDITOR
			var pfm = new ProfilerMarker( "Initialize Cilbox" );
			pfm.Auto();
#endif

			if( initialized ) return;
			initialized = true;
			//Debug.Log( "Cilbox Initialize Metadata:" + assemblyData.Length );

			(Dictionary<String, Serializee> classData, Dictionary<String, Serializee> metaData) = DeserializeAssemblyData(assemblyData);

			InitializeClassStructures(classData);

			LoadClasses(classData);

            LoadMetaData(metaData);

            if ( !bSimulate )
			{
				RunStaticConstructors();
			}
		}

		public CilboxClass GetClass( String className )
		{
			if( className == null ) return null;
			int clsid;
			if( classes.TryGetValue(className, out clsid)) return classesList[clsid];
			return null;
		}

		public object InterpretIID( CilboxClass cls, CilboxProxy ths, ImportFunctionID iid, object [] parameters )
		{
			if( cls == null ) return null;
			uint index = cls.importFunctionToId[(uint)iid];
			if( index == 0xffffffff ) return null;

			object ret = cls.methods[index].Interpret( ths, parameters );

			return ret;
		}

		public bool InterpreterEntry( CilboxMethod m )
		{
			// Use of Monitor.Lock's here slows the whole emulator down by about 8%
			// TODO: Consider some sort of lockless approach.  This is tricky because
			// you need to make sure you interlock both depth, and, time accounting.
			long now = System.Diagnostics.Stopwatch.GetTimestamp();
			Monitor.Enter( this );
			if( ++interpreterAccountingDepth == 1 )
			{
				// First entry, if we've been disabled, quiety abort.
				// this is normal if
				if( disabled )
				{
					--interpreterAccountingDepth;
					Monitor.Exit( this );
					return false;
				}
				interpreterInstructionsCount = 0;
				interpreterAccountingDropDead = now + timeoutLengthUs * interpreterTicksInUs - interpreterAccountingCumulitiveTicks;
				Monitor.Exit( this );
				return true;
			}
			else if( disabled )
			{
				// fault from within, abort now.
				Monitor.Exit( this );
				throw new Exception( $"Function interpreation happened while box was disabled. This should not be possible. Offender: {m.parentClass.className} {m.fullSignature}" );
			}
			else
			{
				if( now > interpreterAccountingDropDead )
				{
					interpreterAccountingCumulitiveTicks = now + timeoutLengthUs * interpreterTicksInUs - interpreterAccountingDropDead;
					--interpreterAccountingDepth;
					Monitor.Exit( this );
					throw new Exception( $"Function {m.parentClass.className} {m.fullSignature} timed out." );
				}

				// Otherwise we are recursively being called. All is well.
				Monitor.Exit( this );
				return true;
			}
		}

		public void InterpreterExit()
		{
			Monitor.Enter( this );
			if( --interpreterAccountingDepth == 0 )
			{
				long now = System.Diagnostics.Stopwatch.GetTimestamp();
				long elapsed = now + timeoutLengthUs * interpreterTicksInUs - interpreterAccountingDropDead - interpreterAccountingCumulitiveTicks;
				interpreterAccountingCumulitiveTicks = now + timeoutLengthUs * interpreterTicksInUs - interpreterAccountingDropDead;

				// For profiling
				if( showFunctionProfiling )
				{
					Monitor.Exit( this );
					Debug.Log( $"{interpreterInstructionsCount} in {elapsed/10}us or {interpreterInstructionsCount*10.0/(double)elapsed}MHz" );
					return;
				}
			}
			Monitor.Exit( this );
		}

		void Update()
		{
			usSpentLastFrame = Interlocked.Exchange( ref interpreterAccountingCumulitiveTicks, 0 ) / interpreterTicksInUs;
		}

        private (Dictionary<string, Serializee> classData, Dictionary<string, Serializee> metaData) DeserializeAssemblyData(string assemblyData)
        {
            Dictionary<String, Serializee> assemblyRoot = new Serializee(Convert.FromBase64String(assemblyData), Serializee.ElementType.Map).AsMap();
            return (assemblyRoot["classes"].AsMap(), assemblyRoot["metadata"].AsMap());
        }

		private void LoadMetaData(Dictionary<string, Serializee> metaData)
		{
            metadatas = new CilMetadataTokenInfo[metaData.Count + 1]; // element 0 is invalid.
            metadatas[0] = new CilMetadataTokenInfo(0);
            metadatas[0].Name = "<INVALID>";

            foreach (var v in metaData)
            {
                int mid = Convert.ToInt32((String)v.Key);
                Dictionary<String, Serializee> st = v.Value.AsMap();
                MetaTokenType metatype = (MetaTokenType)Convert.ToInt32(st["mt"].AsString());
                CilMetadataTokenInfo t = metadatas[mid] = new CilMetadataTokenInfo(metatype);

                t.type = metatype;
                t.Name = "<UNKNOWN>";

                switch (metatype)
                {
                    case MetaTokenType.mtString:
                        t.Name = st["s"].AsString();
                        break;
                    case MetaTokenType.mtArrayInitializer:
                        t.arrayInitializerData = st["data"].AsBlob();
                        break;
                    case MetaTokenType.mtField:
                        // The type has been "sealed" so-to-speak. In that we have an index for it.

                        t.Name = st["name"].AsString();
                        t.declaringTypeName = usage.GetNativeTypeNameFromSerializee(st["dt"]);
                        t.fieldIsStatic = Convert.ToInt32(st["isStatic"].AsString()) != 0;

                        if (st.ContainsKey("index"))
                        {
                            t.fieldIndex = Convert.ToInt32(st["index"].AsString());
                        }
                        else
                        {
                            throw new Exception($"Currently cannot reference fields outside of the cilbox. {t.declaringTypeName} in {v.Key}.  Use properties.");
                        }

                        t.isValid = true;
                        break;
                    case MetaTokenType.mtType:
                        {
							ProcessMetaType(st, t);
                            break;
                        }
                    case MetaTokenType.mtMethod:
                        {
							ProcessMetaMethod(st, t);
                            break;
                        }
                }
            }
        }

		private void InitializeClassStructures(Dictionary<string, Serializee> classData)
		{
            int clsid = 0;
            classes = new Dictionary<String, int>();
            classesList = new CilboxClass[classData.Count];

            foreach (var v in classData)
            {
                CilboxClass cls = new CilboxClass();
                classesList[clsid] = cls;
                classes[(String)v.Key] = clsid;
                clsid++;
            }
        }

		private void LoadClasses(Dictionary<string, Serializee> classData)
		{
            int clsid = 0;
            foreach (var v in classData)
                classesList[clsid++].LoadCilboxClass(this, v.Key, v.Value);
        }

		private void ProcessMetaType(Dictionary<string, Serializee> st, CilMetadataTokenInfo t)
		{
            {
                Serializee typ = st["dt"];
                t.nativeType = usage.GetNativeTypeFromSerializee(typ);
                StackType seType = StackElement.StackTypeFromType(t.nativeType);
                if (seType < StackType.Object)
                {
                    t.nativeTypeIsStackType = true;
                    t.nativeTypeStackType = seType;
                    t.Name = t.nativeType.ToString();
                }
                else if (t.nativeType != null)
                {
                    t.isValid = true;
                    t.Name = "Type: " + typ.AsMap()["n"].AsString();
                }
                else
                {
                    // Maybe it's a type inside our cilbox?
                    t.isValid = false;
                    foreach (CilboxClass c in classesList)
                    {
                        if (c.className == typ.AsMap()["n"].AsString())
                        {
                            t.Name = c.className;
                            t.nativeTypeIsCilboxProxy = true;
                            t.isValid = true;
                        }
                    }

                    if (!t.isValid)
                        Debug.LogError($"Error: Could not find type: {typ.AsMap()["n"].AsString()}");
                }
            }
        }

		private void ProcessMetaMethod(Dictionary<string, Serializee> st, CilMetadataTokenInfo t)
		{
            String name = st["name"].AsString();
            String fullSignature = st["fullSignature"].AsString();
            bool isStatic = Convert.ToInt32(st["isStatic"].AsString()) != 0;
            String useAssembly = st["assembly"].AsString();
            Serializee[] genericArguments = null;
            t.Name = "Method: " + name;

            //Possibly get genericArguments
            Serializee temp;
            if (st.TryGetValue("ga", out temp))
                genericArguments = temp.AsArray();
            else
                genericArguments = new Serializee[0];

            if (usage.OptionallyOverride(name, st["dt"], fullSignature, isStatic, genericArguments, ref t))
            {
                return;
            }

            Serializee stDt;
            (name, stDt) = usage.HandleEarlyMethodRewrite(name, st["dt"], genericArguments);

            string declaringTypeName = t.declaringTypeName = usage.GetNativeTypeNameFromSerializee(stDt);


            Serializee[] parametersSer = null;
            if (st.TryGetValue("parameters", out temp))
                parametersSer = temp.AsArray();
            else
                parametersSer = new Serializee[0];

            // First, see if this is to a class we are responsible for. Like does it come from _this_ class?
            int classid;
            if (declaringTypeName == null)
            {
                Debug.LogError($"Error: Could not find internal type in {fullSignature}");
            }
            else if (classes.TryGetValue(declaringTypeName, out classid))
            {
                CilboxClass matchingClass = classesList[classid];
                uint imid = 0;
                if (matchingClass.methodFullSignatureToIndex.TryGetValue(fullSignature, out imid))
                {
                    //t.nativeToken = 0; // Sentinel for saying it's a cilbox'd class.
                    t.isNative = false;
                    t.interpretiveMethod = (int)imid;
                    t.interpretiveMethodClass = classid;
                    t.isValid = true;
                }
                else
                {
                    t.isValid = false;
                    throw new Exception($"Error: Could not find internal method {declaringTypeName}:{fullSignature}");
                }
            }
            else
            {
                Type declaringType = usage.GetNativeTypeFromSerializee(stDt);
                if (declaringType == null)
                    throw new Exception($"Error: Could not find referenced type {useAssembly}/{declaringTypeName}/");

                MethodBase m = usage.GetNativeMethodFromTypeAndName(declaringType, name, parametersSer,
                    genericArguments, fullSignature);

                if (m != null)
                {
                    t.nativeMethod = m;
                    t.isNative = true;
                    t.isValid = true;
                }
                else if (!t.isNative)
                {
                    throw new Exception("Error: Could not find reference to: [" + useAssembly + "][" + declaringType.FullName + "][" + fullSignature + "] Type from:" + declaringTypeName);
                }
            }
        }

		private void RunStaticConstructors()
		{
            foreach (var c in classesList)
            {
                // This class is loaded as it can be.  Time to call the class ctor, if one exists.
                uint cctorIndex = 0;
                if (c.methodFullSignatureToIndex.TryGetValue("Void .cctor()", out cctorIndex))
                {
                    if (c.methods[cctorIndex].isStatic)
                    {
                        c.methods[cctorIndex].Interpret(null, new object[0]);
                    }
                }
            }
        }
    }
}

