using Cilbox;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Cilbox
{
    public class CilboxMethod
    {
        public CilboxClass parentClass;
        public int MaxStackSize;
        public String methodName;
        public String fullSignature;
        public String[] methodLocals;
        public bool isStatic;
        public Type[] typeLocals;
        public byte[] byteCode;
        public bool isVoid;
        public String[] signatureParameters;
        public Type[] typeParameters;
        public CilboxExceptionHandlingClause[] exceptionClauses;
        public bool hasExceptionClauses = false;

#if UNITY_EDITOR
        ProfilerMarker perfMarkerInterpret;
#endif

        public void Load(CilboxClass cclass, String name, Serializee payload)
        {
            methodName = name;
            parentClass = cclass;
            Dictionary<String, Serializee> methodProps = payload.AsMap();

            Serializee[] vl = methodProps["locals"].AsArray();
            methodLocals = new String[vl.Length];
            typeLocals = new Type[vl.Length];
            int iid = 0;
            for (int i = 0; i < vl.Length; i++)
            {
                Dictionary<String, Serializee> local = vl[i].AsMap();
                methodLocals[iid] = local["name"].AsString();
                typeLocals[iid] = parentClass.box.usage.GetNativeTypeFromSerializee(local["dt"]);
                iid++;
            }

            byteCode = methodProps["body"].AsBlob();

            MaxStackSize = Convert.ToInt32((methodProps["maxStack"].AsString()));
            isVoid = Convert.ToInt32((methodProps["isVoid"].AsString())) != 0;
            isStatic = Convert.ToInt32((methodProps["isStatic"].AsString())) != 0;
            fullSignature = methodProps["fullSignature"].AsString();

            Serializee[] od = methodProps["parameters"].AsArray();
            signatureParameters = new String[od.Length];
            typeParameters = new Type[od.Length];
            int sn = 0;
            for (int p = 0; p < od.Length; p++)
            {
                Dictionary<String, Serializee> thisp = od[p].AsMap();
                signatureParameters[sn] = thisp["name"].AsString();
                typeParameters[sn] = parentClass.box.usage.GetNativeTypeFromSerializee(thisp["dt"]);
                sn++;
            }

            if (methodProps.TryGetValue("eh", out Serializee ehArray))
            {
                Serializee[] ehc = ehArray.AsArray();
                exceptionClauses = new CilboxExceptionHandlingClause[ehc.Length];
                for (int e = 0; e < ehc.Length; e++)
                {
                    Dictionary<String, Serializee> thisehc = ehc[e].AsMap();
                    CilboxExceptionHandlingClause clause = new CilboxExceptionHandlingClause();
                    clause.Flags = (ExceptionHandlingClauseOptions)Convert.ToInt32(thisehc["flags"].AsString());
                    clause.TryOffset = Convert.ToInt32(thisehc["tryOff"].AsString());
                    clause.TryLength = Convert.ToInt32(thisehc["tryLen"].AsString());
                    clause.TryEndOffset = clause.TryOffset + clause.TryLength;
                    clause.HandlerOffset = Convert.ToInt32(thisehc["hOff"].AsString());
                    clause.HandlerLength = Convert.ToInt32(thisehc["hLen"].AsString());
                    if (thisehc.ContainsKey("cType"))
                    {
                        clause.CatchType = parentClass.box.usage.GetNativeTypeFromSerializee(thisehc["cType"]);
                    }
                    exceptionClauses[e] = clause;
                }
                hasExceptionClauses = exceptionClauses.Length > 0;

                Array.Sort(exceptionClauses, CompareExceptionClausesLengthDesc);
            }

#if UNITY_EDITOR
            perfMarkerInterpret = new ProfilerMarker(parentClass.className + ":" + fullSignature);
#endif

            static int CompareExceptionClausesLengthDesc(CilboxExceptionHandlingClause a, CilboxExceptionHandlingClause b)
            {
                return b.TryLength.CompareTo(a.TryLength);
            }
        }

        public object Interpret(CilboxProxy ths, object[] parametersIn)
        {
            StackElement[] parameters;
            StackElement[] stackBuffer = new StackElement[Cilbox.defaultStackSize];

            int plen = 0;
            if (parametersIn != null)
            {
                plen = parametersIn.Length;
            }

            if (isStatic)
            {
                parameters = new StackElement[plen];
                for (int p = 0; p < plen; p++)
                    parameters[p].Load(parametersIn[p]);
            }
            else
            {
                parameters = new StackElement[plen + 1];
                parameters[0].Load(ths);
                for (int p = 0; p < plen; p++)
                    parameters[p + 1].Load(parametersIn[p]);
                plen++;
            }

            object ret = null;
            if (!parentClass.box.InterpreterEntry(this)) return null;
            try
            {
                ret = InterpretInner(stackBuffer, parameters).AsObject();
            }
            catch (Exception e)
            {
                parentClass.box.InterpreterExit();
                Debug.Log(e.ToString());
                throw;
            }
            parentClass.box.InterpreterExit();
            return ret;
        }

        private StackElement InterpretInner(ArraySegment<StackElement> stackBufferIn, ArraySegment<StackElement> parametersIn)
        {
            Span<StackElement> stackBuffer = stackBufferIn.AsSpan();
            Span<StackElement> parameters = parametersIn.AsSpan();
            Stack<int> handlerClauseStack = null; // don't allocate unless necessary

#if UNITY_EDITOR
            perfMarkerInterpret.Begin();
#endif

            Cilbox box = parentClass.box;

            int localVarsHead = MaxStackSize;
            int stackContinues = localVarsHead + methodLocals.Length;

            // Uncomment for debugging.
#if false
			bool bDeepDebug = false;
			if( parentClass.className.Contains("TestScript2") )//fullSignature.Contains( "TestScript2" ) )
			{
				bDeepDebug = true;
				String parmSt = ""; for( int sk = 0; sk < parameters.Length; sk++ ) {
					parmSt += "/"; parmSt += parameters[sk].AsObject() + "+" + parameters[sk].type;
				}
				Debug.Log( "***** FUNCTION ENTRY " + parentClass.className + " " + methodName + " " + parametersIn.Offset + " PARM:" + parmSt);
				bDeepDebug = true;
			}
#endif
            int sp = -1;
            bool cont = true;
            int pc = 0;
            try
            {
                do
                {
                    // While this is not threadsafe, that's OK.  This is more for broad strokes.
                    // We don't have to worry about critical pieces going in/out of a race condition
                    // for instance, interpreterAccountingLastStart can't go wonky on us.
                    //
                    // If you use Interlocked.Add() it slows the whole emulator down by about 40%!
                    long steps = ++box.interpreterInstructionsCount;
                    if ((steps & 0x3f) == 0)
                    {
                        long now = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (now > box.interpreterAccountingDropDead)
                        {
                            box.interpreterAccountingCumulitiveTicks = now + box.timeoutLengthUs * box.interpreterTicksInUs - box.interpreterAccountingDropDead;
                            cont = false;
                            throw new Exception("Script time resources overutilized @ " + pc + " In " + methodName + " (Timeout Us: " + box.interpreterAccountingCumulitiveTicks / box.interpreterTicksInUs + "/" + box.timeoutLengthUs + " )");
                        }
                    }

                    byte b = byteCode[pc];

#if false
					// Uncomment for debugging.
					if( bDeepDebug )
					{
						String stackSt = ""; for( int sk = 0; sk < stackBufferIn.Count; sk++ ) { stackSt += "/"; if( sk == sp ) stackSt += ">"; stackSt += stackBuffer[sk].AsObject() + "+" + stackBuffer[sk].type; if( sk == sp ) stackSt += "<"; }
						int icopy = pc; CilboxUtil.OpCodes.OpCode opc = CilboxUtil.OpCodes.ReadOpCode ( byteCode, ref icopy );
						Debug.Log( "Bytecode " + opc + " (" + b.ToString("X2") + ") @ " + pc + "/" + byteCode.Length + " " + stackSt);
					}
#endif
                    // For itty bitty profiling.

#if PER_INSTRUCTION_PROFILING // Opcode profiling
int xicopy = pc; CilboxUtil.OpCodes.OpCode opcx = CilboxUtil.OpCodes.ReadOpCode ( byteCode, ref xicopy );
var spiperf = new ProfilerMarker(opcx.ToString());
spiperf.Begin();
#endif

                    pc++;
                    switch (b)
                    {
                        case 0x00: break; // nop
                        case 0x01: throw new Exception($"Debug Break @ {pc}"); // break
                        case 0x02: stackBuffer[++sp] = parameters[0]; break; //ldarg.0
                        case 0x03: stackBuffer[++sp] = parameters[1]; break; //ldarg.1
                        case 0x04: stackBuffer[++sp] = parameters[2]; break; //ldarg.2
                        case 0x05: stackBuffer[++sp] = parameters[3]; break; //ldarg.3
                        case 0x06: stackBuffer[++sp] = stackBuffer[localVarsHead + 0]; break; //ldloc.0
                        case 0x07: stackBuffer[++sp] = stackBuffer[localVarsHead + 1]; break; //ldloc.1
                        case 0x08: stackBuffer[++sp] = stackBuffer[localVarsHead + 2]; break; //ldloc.2
                        case 0x09: stackBuffer[++sp] = stackBuffer[localVarsHead + 3]; break; //ldloc.3
                        case 0x0a: stackBuffer[localVarsHead + 0] = stackBuffer[sp--]; break; //stloc.0
                        case 0x0b: stackBuffer[localVarsHead + 1] = stackBuffer[sp--]; break; //stloc.1
                        case 0x0c: stackBuffer[localVarsHead + 2] = stackBuffer[sp--]; break; //stloc.2
                        case 0x0d: stackBuffer[localVarsHead + 3] = stackBuffer[sp--]; break; //stloc.3
                        case 0x0e: stackBuffer[++sp] = parameters[byteCode[pc++]]; break; // ldarg.s <uint8 (argNum)>
                        case 0x0f: stackBuffer[++sp] = StackElement.CreateReference(parametersIn.Array, (uint)parametersIn.Offset + (uint)byteCode[pc++]); break; // ldarga.s <uint8 (argNum)>
                        case 0x11: stackBuffer[++sp] = stackBuffer[localVarsHead + byteCode[pc++]]; break; //ldloc.s
                        case 0x12:
                            {
                                uint whichLocal = byteCode[pc++];
                                stackBuffer[++sp] = StackElement.CreateReference(stackBufferIn.Array, (uint)(localVarsHead + whichLocal + stackBufferIn.Offset));
                                break; //ldloca.s // Load address of local variable.
                            }
                        case 0x13: stackBuffer[localVarsHead + byteCode[pc++]] = stackBuffer[sp--]; break; //stloc.s
                        case 0x14: stackBuffer[++sp].LoadObject(null); break; // ldnull
                        case 0x15: stackBuffer[++sp].LoadInt(-1); break; // ldc.i4.m1
                        case 0x16: stackBuffer[++sp].LoadInt(0); break; // ldc.i4.0
                        case 0x17: stackBuffer[++sp].LoadInt(1); break; // ldc.i4.1
                        case 0x18: stackBuffer[++sp].LoadInt(2); break; // ldc.i4.2
                        case 0x19: stackBuffer[++sp].LoadInt(3); break; // ldc.i4.3
                        case 0x1a: stackBuffer[++sp].LoadInt(4); break; // ldc.i4.4
                        case 0x1b: stackBuffer[++sp].LoadInt(5); break; // ldc.i4.5
                        case 0x1c: stackBuffer[++sp].LoadInt(6); break; // ldc.i4.6
                        case 0x1d: stackBuffer[++sp].LoadInt(7); break; // ldc.i4.7
                        case 0x1e: stackBuffer[++sp].LoadInt(8); break; // ldc.i4.8

                        case 0x1f: stackBuffer[++sp].LoadInt((sbyte)byteCode[pc++]); break; // ldc.i4.s <int8>
                        case 0x20: stackBuffer[++sp].LoadInt((int)BytecodeAsU32(ref pc)); break; // ldc.i4 <int32>
                        case 0x21: stackBuffer[++sp].LoadLong((long)BytecodeAs64(ref pc)); break; // ldc.i8 <int64>
                        case 0x22: stackBuffer[++sp].LoadFloat(CilboxUtil.IntFloatConverter.ConvertUtoF(BytecodeAsU32(ref pc))); break; // ldc.r4 <float32 (num)>
                        case 0x23: stackBuffer[++sp].LoadDouble(CilboxUtil.IntFloatConverter.ConvertEtoD(BytecodeAs64(ref pc))); break; // ldc.r8 <float64 (num)>
                                                                                                                                        // 0x24 does not exist.
                        case 0x25: stackBuffer[sp + 1] = stackBuffer[sp]; sp++; break; // dup TODO: Does dup potentially duplicate objects somehow?
                        case 0x26: sp--; break; // pop

                        case 0x27: //jmp
                        case 0x28: //call
                        case 0x29: //calli
                        case 0x73: //newobj
                        case 0x6F: //callvirt
                            {
                                uint bc = (b == 0x29) ? stackBuffer[sp--].u : BytecodeAsU32(ref pc);
                                object iko = null; // Returned value.
                                CilMetadataTokenInfo dt = box.metadatas[bc];
                                bool isVoid = false;
                                MethodBase st;

                                if (!dt.isValid)
                                {
                                    throw new Exception("Error, function " + dt.Name + " Not found in " + parentClass.className + ":" + fullSignature);
                                }

                                if (!dt.isNative)
                                {
                                    if (dt.shim != null)
                                    {
                                        isVoid = dt.shimIsVoid;
                                        int staticOffset = dt.shimIsStatic ? 0 : 1;
                                        int numParams = dt.shimParameterCount;
                                        int nextParameterStart = stackContinues;
                                        int nextStackHead = nextParameterStart + numParams + staticOffset;

                                        for (int i = numParams - 1; i >= 0; i--)
                                            stackBuffer[nextParameterStart + i + staticOffset] = stackBuffer[sp--];
                                        if (!dt.shimIsStatic)
                                            stackBuffer[nextParameterStart] = stackBuffer[sp--];

                                        if (!isVoid)
                                            stackBuffer[++sp] = dt.shim(dt, stackBufferIn.Slice(nextStackHead), stackBufferIn.Slice(nextParameterStart, numParams + staticOffset));
                                        else
                                            dt.shim(dt, stackBufferIn.Slice(nextStackHead), stackBufferIn.Slice(nextParameterStart, numParams + staticOffset));

                                    }
                                    else
                                    {
                                        // Sentinel.  interpretiveMethod will contain what method to interpret.
                                        // interpretiveMethodClass
                                        CilboxClass targetClass = box.classesList[dt.interpretiveMethodClass];
                                        CilboxMethod targetMethod = targetClass.methods[dt.interpretiveMethod];
                                        isVoid = targetMethod.isVoid;
                                        if (targetMethod == null)
                                            throw (new Exception($"Function {dt.Name} not found"));

                                        int staticOffset = (targetMethod.isStatic ? 0 : 1);
                                        int numParams = targetMethod.signatureParameters.Length;

                                        int nextParameterStart = stackContinues;
                                        int nextStackHead = nextParameterStart + numParams + staticOffset;

                                        for (int i = numParams - 1; i >= 0; i--)
                                            stackBuffer[nextParameterStart + i + staticOffset] = stackBuffer[sp--];

                                        if (!targetMethod.isStatic)
                                            stackBuffer[nextParameterStart] = stackBuffer[sp--];

                                        if (!isVoid)
                                            stackBuffer[++sp] = targetMethod.InterpretInner(stackBufferIn.Slice(nextStackHead), stackBufferIn.Slice(nextParameterStart, numParams + staticOffset));
                                        else
                                            targetMethod.InterpretInner(stackBufferIn.Slice(nextStackHead), stackBufferIn.Slice(nextParameterStart, numParams + staticOffset));

                                        if (b == 0x27)
                                        {
                                            // This is returning from a jump, so immediately abort.
                                            if (isVoid) stackBuffer[++sp] = StackElement.nil; /// ?? Please check me! If wrong, fix above, too.
                                            cont = false;
                                        }
                                    }
                                }
                                else
                                {
                                    st = dt.nativeMethod;
                                    if (st is MethodInfo)
                                        isVoid = ((MethodInfo)st).ReturnType == typeof(void);

                                    ParameterInfo[] pa = st.GetParameters();
                                    int numFields = pa.Length;
                                    object callthis = null;
                                    object[] callpar = new object[numFields];
                                    StackElement callthis_se = new StackElement { };
                                    StackElement[] callpar_se = new StackElement[numFields];
                                    int ik;
                                    for (ik = 0; ik < numFields; ik++)
                                    {
                                        StackElement se = stackBuffer[sp--];
                                        callpar_se[numFields - ik - 1] = se;
                                        object o = se.AsObject();
                                        Type t = pa[numFields - ik - 1].ParameterType;

                                        // XXX TODO: Copy mechanism below from ResolveToStackElement and Coerce
                                        if (se.type < StackType.Object)
                                        {
                                            if (o != null && t.IsValueType && o.GetType() != t)
                                            {
                                                //o = Convert.ChangeType( o, t );
                                                o = se.CoerceToObject(t);
                                            }
                                        }
                                        callpar[numFields - ik - 1] = o;
                                    }
                                    if (st.IsConstructor)
                                    {
                                        // TRICKY: This generally can only be arrived at when scripts run their own constructors.
                                        if (st.DeclaringType == typeof(MonoBehaviour))
                                            iko = this;
                                        else // Otherwise it's normal.
                                            iko = ((ConstructorInfo)st).Invoke(callpar);
                                    }
                                    else if (!st.IsStatic)
                                    {
                                        MethodInfo mi = (MethodInfo)st;
                                        StackElement seorig = stackBuffer[sp--];
                                        StackElement se = StackElement.ResolveToStackElement(seorig);
                                        Type t = mi.DeclaringType;

                                        if (t.IsValueType && se.type < StackType.Object)
                                        {
                                            // Try to coerce types.
                                            callthis = se.CoerceToObject(t);
                                        }
                                        else
                                        {
                                            callthis = se.o;
                                        }

                                        iko = st.Invoke(callthis, callpar);
                                        if (seorig.type == StackType.Address)
                                        {
                                            seorig.DereferenceLoad(callthis);
                                        }
                                    }
                                    else
                                    {
                                        iko = st.Invoke(null, callpar);
                                    }

                                    // Possibly copy back any references.
                                    for (ik = 0; ik < numFields; ik++)
                                    {
                                        StackElement se = callpar_se[ik];
                                        if (se.type == StackType.Address)
                                        {
                                            callpar_se[ik].DereferenceLoad(callpar[ik]);
                                            //if( se.o.GetType() == typeof(StackElement[]) )
                                            //	((StackElement[])se.o)[se.i].Load( callpar[ik] );
                                            //else
                                            //	((Array)se.o).SetValue(callpar[ik], se.i);
                                        }
                                    }

                                    if (!isVoid)
                                    {
                                        stackBuffer[++sp].Load(iko);
                                    }
                                    if (b == 0x27)
                                    {
                                        // This is returning from a jump, so immediately abort.
                                        if (isVoid) stackBuffer[++sp] = StackElement.nil; /// ?? Please check me! If wrong, fix above, too.
                                        cont = false;
                                    }
                                }

                                break;
                            }
                        case 0x2a: cont = false; break; // ret

                        case 0x2b: pc += (sbyte)byteCode[pc] + 1; break; //br.s
                        case 0x38: { int ofs = (int)BytecodeAsU32(ref pc); pc += ofs; break; } // br

                        case 0xdd: // leave
                        case 0xde: // leave.s
                            {
                                int currentInstruction = pc;
                                sp = -1; // leave(.s) clears the stack.
                                int offset = (b == 0xde) ? (sbyte)byteCode[pc++] : (int)BytecodeAsU32(ref pc);
                                int leaveTarget = pc + offset;

                                // early out if no exception clauses.
                                if (!hasExceptionClauses)
                                {
                                    pc = leaveTarget;
                                    break;
                                }

                                if (handlerClauseStack == null)
                                {
                                    handlerClauseStack = new Stack<int>();
                                }

                                handlerClauseStack.Push(leaveTarget);
                                for (int i = 0; i < exceptionClauses.Length; i++)
                                {
                                    CilboxExceptionHandlingClause c = exceptionClauses[i];

                                    // only handling Finally for now
                                    // todo: add catch handling?
                                    if (c.Flags != ExceptionHandlingClauseOptions.Finally)
                                    {
                                        continue;
                                    }

                                    // Check we are in bounds of the Try block.
                                    if (currentInstruction < c.TryOffset || currentInstruction >= c.TryEndOffset)
                                    {
                                        continue;
                                    }

                                    // Verify leaveTarget is outside the try block.
                                    if (leaveTarget >= c.TryOffset && leaveTarget < c.TryEndOffset)
                                    {
                                        continue;
                                    }

                                    handlerClauseStack.Push(c.HandlerOffset);
                                }

                                // Continue to the leave target or innermost handler.
                                pc = handlerClauseStack.Pop();
                                break;
                            }

                        case 0xdc: // endfault, endfinally
                            {
                                if (handlerClauseStack == null || handlerClauseStack.Count == 0)
                                {
                                    throw new Exception("endfinally without a matching target.");
                                }
                                pc = handlerClauseStack.Pop();
                                break;
                            }

                        case 0x2c:
                        case 0x39: // brfalse.s, brnull.s, brzero.s - is it zero, null or  / brfalse
                        case 0x2d:
                        case 0x3a: // brinst.s, brtrue.s / btrue
                            {
                                StackElement s = stackBuffer[sp--];
                                int iop = b - 0x2c;
                                if (b >= 0x38) iop -= 0xd;
                                int offset = (b >= 0x38) ? (int)BytecodeAsU32(ref pc) : (sbyte)byteCode[pc++];
                                switch (iop)
                                {
                                    case 0: if ((s.type == StackType.Object && s.o == null) || (s.type != StackType.Object && s.i == 0)) pc += offset; break;
                                    case 1: if ((s.type == StackType.Object && s.o != null) || (s.type != StackType.Object && s.i != 0)) pc += offset; break;
                                }
                                break;
                            }
                        case 0x2e:
                        case 0x3b: // beq.s / beq
                        case 0x2f:
                        case 0x3c: // bge.s
                        case 0x30:
                        case 0x3d: // bgt.s
                        case 0x31:
                        case 0x3e: // ble.s
                        case 0x32:
                        case 0x3f: // blt.s
                        case 0x33:
                        case 0x40: // bne.un.s
                        case 0x34:
                        case 0x41: // bge.un.s
                        case 0x35:
                        case 0x42: // bgt.un.s
                        case 0x36:
                        case 0x43: // ble.un.s
                        case 0x37:
                        case 0x44: // blt.un.s
                            {
                                StackElement sb = stackBuffer[sp--]; StackElement sa = stackBuffer[sp--];
                                int iop = b - 0x2e;
                                if (b >= 0x38) iop -= 0xd;
                                int joffset = (b >= 0x38) ? (int)BytecodeAsU32(ref pc) : (sbyte)byteCode[pc++];

                                StackType promoted = StackElement.StackTypeMaxPromote(sa.type, sb.type);

                                switch (promoted)
                                {
                                    case StackType.Sbyte:
                                    case StackType.Short:
                                    case StackType.Int:
                                        switch (iop)
                                        {
                                            case 0: if (sa.i == sb.i) pc += joffset; break;
                                            case 1: if (sa.i >= sb.i) pc += joffset; break;
                                            case 2: if (sa.i > sb.i) pc += joffset; break;
                                            case 3: if (sa.i <= sb.i) pc += joffset; break;
                                            case 4: if (sa.i < sb.i) pc += joffset; break;
                                            case 5: if (sa.e != sb.e) pc += joffset; break;
                                            case 6: if (sa.e >= sb.e) pc += joffset; break;
                                            case 7: if (sa.e > sb.e) pc += joffset; break;
                                            case 8: if (sa.e <= sb.e) pc += joffset; break;
                                            case 9: if (sa.e < sb.e) pc += joffset; break;
                                        }
                                        break;
                                    case StackType.Byte:
                                    case StackType.Ushort:
                                    case StackType.Uint:
                                    case StackType.Ulong:
                                        switch (iop)
                                        {
                                            case 0: if (sa.e == sb.e) pc += joffset; break;
                                            case 1: if (sa.e >= sb.e) pc += joffset; break;
                                            case 2: if (sa.e > sb.e) pc += joffset; break;
                                            case 3: if (sa.e <= sb.e) pc += joffset; break;
                                            case 4: if (sa.e < sb.e) pc += joffset; break;
                                            case 5: if (sa.e != sb.e) pc += joffset; break;
                                            case 6: if (sa.e >= sb.e) pc += joffset; break;
                                            case 7: if (sa.e > sb.e) pc += joffset; break;
                                            case 8: if (sa.e <= sb.e) pc += joffset; break;
                                            case 9: if (sa.e < sb.e) pc += joffset; break;
                                        }
                                        break;
                                    case StackType.Long:
                                        switch (iop)
                                        {
                                            case 0: if (sa.l == sb.l) pc += joffset; break;
                                            case 1: if (sa.l >= sb.l) pc += joffset; break;
                                            case 2: if (sa.l > sb.l) pc += joffset; break;
                                            case 3: if (sa.l <= sb.l) pc += joffset; break;
                                            case 4: if (sa.l < sb.l) pc += joffset; break;
                                            case 5: if (sa.e != sb.e) pc += joffset; break;
                                            case 6: if (sa.e >= sb.e) pc += joffset; break;
                                            case 7: if (sa.e > sb.e) pc += joffset; break;
                                            case 8: if (sa.e <= sb.e) pc += joffset; break;
                                            case 9: if (sa.e < sb.e) pc += joffset; break;
                                        }
                                        break;
                                    case StackType.Float:
                                        switch (iop)
                                        {
                                            case 0: if (sa.f == sb.f) pc += joffset; break;
                                            case 1: if (sa.f >= sb.f) pc += joffset; break;
                                            case 2: if (sa.f > sb.f) pc += joffset; break;
                                            case 3: if (sa.f <= sb.f) pc += joffset; break;
                                            case 4: if (sa.f < sb.f) pc += joffset; break;
                                            case 5: if (sa.f != sb.f) pc += joffset; break;
                                            case 6: if (sa.f >= sb.f) pc += joffset; break;
                                            case 7: if (sa.f > sb.f) pc += joffset; break;
                                            case 8: if (sa.f <= sb.f) pc += joffset; break;
                                            case 9: if (sa.f < sb.f) pc += joffset; break;
                                        }
                                        break;
                                    case StackType.Double:
                                        switch (iop)
                                        {
                                            case 0: if (sa.d == sb.d) pc += joffset; break;
                                            case 1: if (sa.d >= sb.d) pc += joffset; break;
                                            case 2: if (sa.d > sb.d) pc += joffset; break;
                                            case 3: if (sa.d <= sb.d) pc += joffset; break;
                                            case 4: if (sa.d < sb.d) pc += joffset; break;
                                            case 5: if (sa.d != sb.d) pc += joffset; break;
                                            case 6: if (sa.d >= sb.d) pc += joffset; break;
                                            case 7: if (sa.d > sb.d) pc += joffset; break;
                                            case 8: if (sa.d <= sb.d) pc += joffset; break;
                                            case 9: if (sa.d < sb.d) pc += joffset; break;
                                        }
                                        break;
                                    case StackType.Object:
                                        switch (iop)
                                        {
                                            case 0: if (sa.o == sb.o) pc += joffset; break;
                                            case 5: if (sa.o != sb.o) pc += joffset; break;
                                            default: throw new("Invalid object comparison");
                                        }
                                        break;
                                    default:
                                        throw new("Invalid comparison");
                                }
                                break;
                            }
                        case 0x45: // Switch
                            {
                                int nsw = (int)BytecodeAsU32(ref pc);
                                int startpc = pc;
                                pc += nsw * 4;
                                StackElement s = stackBuffer[sp--];
                                if (s.type > StackType.Ulong)
                                    throw new("Stack type invalid for switch statement");

                                if (s.u < nsw)
                                {
                                    int smatch = (int)(s.u * 4 + startpc);
                                    int ofs = (int)BytecodeAsU32(ref smatch);
                                    pc += ofs;
                                }
                                // Otherwise fall through
                                break;
                            }

                        case 0x58:
                        case 0x59:
                        case 0x5A:
                        case 0x5B:
                        case 0x5C:
                        case 0x5D:
                        case 0x5E:
                        case 0x5F:
                        case 0x60:
                        case 0x61:
                        case 0x62:
                        case 0x63:
                        case 0x64:
                                HandleArithmeticOpCode(b, ref sp, stackBuffer, ref pc);
                                break;

                        case 0x65: stackBuffer[sp].l = -stackBuffer[sp].l; break;
                        case 0x66: stackBuffer[sp].e ^= 0xffffffffffffffff; break;

                        // XXX TODO: Perf improvement, detect float-to-int conversions and fast-path them.
                        // C# Does not want you to blindly interpret these.
                        case 0x67: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadSByte(((se.type < StackType.Float) ? (sbyte)se.u : (sbyte)se.CoerceToObject(typeof(sbyte)))); break; } // conv.i1
                        case 0x68: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadShort(((se.type < StackType.Float) ? (short)se.i : (short)se.CoerceToObject(typeof(short)))); break; } // conv.i2
                        case 0x69: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadInt(((se.type < StackType.Float) ? (int)se.i : (int)se.CoerceToObject(typeof(int)))); break; } // conv.i4
                        case 0x6A: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadLong((se.type <= StackType.Int ? (long)se.i : se.type == StackType.Uint ? (long)se.u : se.type == StackType.Long ? (long)se.l : se.type == StackType.Ulong ? (long)se.e : (long)se.CoerceToObject(typeof(long)))); break; } // conv.i8
                        case 0x6B: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadFloat((se.type <= StackType.Int ? (float)se.i : se.type == StackType.Uint ? (float)se.u : se.type == StackType.Long ? (float)se.l : se.type == StackType.Ulong ? (float)se.e : se.type == StackType.Double ? (float)se.d : (float)se.CoerceToObject(typeof(float)))); break; } // conv.r4
                        case 0x6C: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadDouble((se.type <= StackType.Int ? (double)se.i : se.type == StackType.Uint ? (double)se.u : se.type == StackType.Long ? (double)se.l : se.type == StackType.Ulong ? (double)se.e : se.type == StackType.Float ? (double)se.f : (double)se.CoerceToObject(typeof(double)))); break; } // conv.r8
                        case 0x6D: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadUint(((se.type < StackType.Float) ? (uint)se.u : (uint)se.CoerceToObject(typeof(uint)))); break; } // conv.u4
                        case 0x6E: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadUlong((se.type <= StackType.Int ? (ulong)se.i : se.type == StackType.Uint ? (ulong)se.u : se.type == StackType.Long ? (ulong)se.l : se.type == StackType.Ulong ? (ulong)se.e : (ulong)se.CoerceToObject(typeof(ulong)))); break; } // conv.u8
                        case 0xD1: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadUshort(((se.type < StackType.Float) ? (ushort)se.u : (ushort)se.CoerceToObject(typeof(ushort)))); break; } // conv.u2
                        case 0xD2: { StackElement se = stackBuffer[sp]; stackBuffer[sp].LoadByte(((se.type < StackType.Float) ? (byte)se.u : (byte)se.CoerceToObject(typeof(byte)))); break; } // conv.u1

                        case 0x72:
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                stackBuffer[++sp].Load(box.metadatas[bc].Name);
                                break; //ldstr
                            }
                        case 0x74: //castclass
                        case 0x75: //isinst
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                StackElement se = stackBuffer[sp--];
                                CilMetadataTokenInfo ti = box.metadatas[bc];
                                object oRet = null;
                                if (ti.nativeTypeIsCilboxProxy)
                                {
                                    if (se.o is CilboxProxy)
                                    {
                                        // Both are proxies. Check name.
                                        if (((CilboxProxy)(se.o)).className == ti.Name)
                                            oRet = se.o;
                                    }
                                }
                                else if (ti.nativeTypeIsStackType)
                                {
                                    if (ti.nativeTypeStackType == StackElement.TypeToStackType[ti.Name])
                                        stackBuffer[++sp] = se;
                                }
                                else if (se.o.GetType() == ti.nativeType)
                                    stackBuffer[++sp].LoadObject(se.o);

                                stackBuffer[++sp].LoadObject(oRet);

                                if (b == 0x74 && oRet == null)
                                {
                                    throw new Exception($"Error: casting class invalid to {ti.Name}");
                                }
                                break;
                            }

                        case 0x7a: throw (System.Exception)stackBuffer[sp--].AsObject(); //throw
                        case 0x7b:
                            {
                                uint bc = BytecodeAsU32(ref pc);

                                StackElement se = stackBuffer[sp--];
                                if (se.o is CilboxProxy)
                                    stackBuffer[++sp] = ((CilboxProxy)se.o).fields[box.metadatas[bc].fieldIndex];
                                else
                                    throw new Exception("Unimplemented.  Attempting to get field on non-cilbox object");
                                // Tricky:  Do not allow host-fields without great care. For instance, getting access to PlatformActual.DelegateRepackage would all the program out.
                                break; //ldfld
                            }
                        case 0x7c:
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                StackElement se = stackBuffer[sp--];

                                if (se.o is CilboxProxy)
                                    stackBuffer[++sp] = StackElement.CreateReference((Array)(((CilboxProxy)se.o).fields), (uint)box.metadatas[bc].fieldIndex);
                                else
                                    throw new Exception("Unimplemented.  Attempting to get field on non-cilbox object");
                                break;// ldflda
                            }
                        case 0x7d:
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                StackElement se = stackBuffer[sp--];
                                object opths = stackBuffer[sp--].AsObject();
                                if (opths is CilboxProxy)
                                {
                                    ((CilboxProxy)opths).fields[box.metadatas[bc].fieldIndex] = se;
                                    //Debug.Log( "Type: " + ((CilboxProxy)opths).fields[box.metadatas[bc].fieldIndex].type );
                                }
                                else
                                    throw new Exception("Unimplemented.  Attempting to set field on non-cilbox object");
                                break; //stfld
                            }
                        case 0x7e:
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                stackBuffer[++sp].Load(parentClass.staticFields[box.metadatas[bc].fieldIndex]);
                                break; //ldsfld
                            }
                        case 0x7f:
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                stackBuffer[++sp] = StackElement.CreateReference((Array)(parentClass.staticFields), (uint)box.metadatas[bc].fieldIndex);
                                break;// ldsflda
                            }
                        case 0x80:
                            {
                                uint bc = BytecodeAsU32(ref pc);
                                object obj = stackBuffer[sp--].AsObject();
                                parentClass.staticFields[box.metadatas[bc].fieldIndex] = obj;
                                break; //stsfld
                            }
                        case 0x8C: // box (This pulls off a type)
                            {
                                uint otyp = BytecodeAsU32(ref pc);
                                stackBuffer[sp].LoadObject(stackBuffer[sp].AsObject());//(metaType.nativeType)stackBuffer[sp-1].AsObject();
                                break;
                            }
                        case 0x8d:
                            {
                                uint otyp = BytecodeAsU32(ref pc);
                                if (stackBuffer[sp].type > StackType.Ulong)
                                    throw new Exception("Invalid type, processing new array");
                                int size = stackBuffer[sp].i;
                                Type t = box.metadatas[otyp].nativeType;
                                stackBuffer[sp].LoadObject(Array.CreateInstance(t, size));
                                //newarr <etype>
                                break;
                            }
                        case 0x8e:
                            {
                                stackBuffer[sp].LoadInt(((Array)(stackBuffer[sp].o)).Length);
                                break; //ldlen
                            }
                        case 0x8f:
                            {
                                /*uint whichClass = */
                                BytecodeAsU32(ref pc); // (For now, ignored)
                                uint index = stackBuffer[sp--].u;
                                Array a = (Array)(stackBuffer[sp--].AsObject());
                                stackBuffer[++sp] = StackElement.CreateReference(a, index);
                                break; //ldlema
                            }
                        case 0x90:
                        case 0x91:
                        case 0x92:
                        case 0x93:
                        case 0x94:
                        case 0x95:
                        case 0x96:
                        case 0x97:
                        case 0x98:
                        case 0x99:
                            {
                                if (stackBuffer[sp].type > StackType.Uint) throw new Exception("Invalid index type" + stackBuffer[sp].type + " " + stackBuffer[sp].o);
                                int index = stackBuffer[sp--].i;
                                //						Array a = ((Array)(stackBuffer[sp].o));
                                switch (b - 0x90)
                                {
                                    // Does this way work universally?  Can we assume the compiler knows what it's doing?
                                    // Previously it looked more like a.GetValue( index ).
                                    case 0: stackBuffer[sp].LoadSByte((sbyte)(((sbyte[])stackBuffer[sp].o)[index])); break; // ldelem.i1
                                    case 1: stackBuffer[sp].LoadByte((byte)(((byte[])stackBuffer[sp].o)[index])); break; // ldelem.u1
                                    case 2: stackBuffer[sp].LoadShort((short)(((short[])stackBuffer[sp].o)[index])); break; // ldelem.i2
                                    case 3: stackBuffer[sp].LoadUshort((ushort)(((ushort[])stackBuffer[sp].o)[index])); break; // ldelem.u2
                                    case 4: stackBuffer[sp].LoadInt((int)(((int[])stackBuffer[sp].o)[index])); break; // ldelem.i4
                                    case 5: stackBuffer[sp].LoadUint((uint)(((uint[])stackBuffer[sp].o)[index])); break; // ldelem.u4
                                    case 6: stackBuffer[sp].LoadUlong((ulong)(((ulong[])stackBuffer[sp].o)[index])); break; // ldelem.u8 / ldelem.i8
                                    case 7: stackBuffer[sp].LoadInt((int)(((int[])stackBuffer[sp].o)[index])); break; // ldelem.i
                                    case 8: stackBuffer[sp].LoadFloat((float)(((float[])stackBuffer[sp].o)[index])); break; // ldelem.r4
                                    case 9: stackBuffer[sp].LoadDouble((double)(((double[])stackBuffer[sp].o)[index])); break; // ldelem.r8

                                }
                                break;
                            }
                        case 0x9a:
                            {
                                if (stackBuffer[sp].type > StackType.Uint) throw new Exception("Invalid index type" + stackBuffer[sp].type + " " + stackBuffer[sp].o);
                                int index = stackBuffer[sp--].i;
                                Array a = ((Array)(stackBuffer[sp--].o));
                                stackBuffer[++sp].LoadObject(a.GetValue(index));
                                break; //Ldelem_Ref
                            }
                        case 0x9c:
                            {
                                SByte val = (SByte)stackBuffer[sp--].i;
                                if (stackBuffer[sp].type > StackType.Uint) throw new Exception("Invalid index type" + stackBuffer[sp].type + " " + stackBuffer[sp].o);
                                int index = stackBuffer[sp--].i;
                                ((Array)(stackBuffer[sp--].o)).SetValue((byte)val, index);
                                break; // stelem.i1
                            }
                        case 0xa0:
                            {
                                float val;
                                val = stackBuffer[sp--].f;
                                if (stackBuffer[sp].type > StackType.Uint) throw new Exception("Invalid index type" + stackBuffer[sp].type + " " + stackBuffer[sp].o);
                                int index = stackBuffer[sp--].i;
                                float[] array = (float[])stackBuffer[sp--].AsObject();
                                array[index] = val;
                                break; // stelem.r4
                            }
                        case 0xa2:
                            {
                                object val = stackBuffer[sp--].AsObject();
                                if (stackBuffer[sp].type > StackType.Uint) throw new Exception("Invalid index type");
                                int index = stackBuffer[sp--].i;
                                object[] array = (object[])stackBuffer[sp--].AsObject();
                                array[index] = val;
                                break; // stelem.ref
                            }
                        case 0xa4:
                            {
                                uint otyp = BytecodeAsU32(ref pc);
                                object val = stackBuffer[sp--].AsObject();
                                if (stackBuffer[sp].type > StackType.Uint) throw new Exception("Invalid index type");
                                int index = stackBuffer[sp--].i;
                                object[] array = (object[])stackBuffer[sp--].AsObject();
                                Type t = box.metadatas[otyp].nativeType;
                                array[index] = Convert.ChangeType(val, t);  // This shouldn't be type changing.s
                                break; // stelem
                            }
                        case 0xA5:
                            {
                                uint otyp = BytecodeAsU32(ref pc); // Let's hope that somehow this isn't needed?
                                CilMetadataTokenInfo metaType = box.metadatas[otyp];
                                if (metaType.nativeTypeIsStackType)
                                {
                                    stackBuffer[sp].Unbox(stackBuffer[sp].AsObject(), metaType.nativeTypeStackType);
                                }
                                else
                                {
                                    throw new Exception($"Scary Unbox (that we don't have code for) from {otyp} ORIG {metaType.ToString()} @ {pc}");
                                }
                                break; // unbox.any
                            }
                        case 0xD0:
                            {
                                uint md = BytecodeAsU32(ref pc); // Let's hope that somehow this isn't needed?
                                CilMetadataTokenInfo mi = box.metadatas[md];
                                object loadedObject = null;
                                switch (mi.type)
                                {
                                    case MetaTokenType.mtField: // Get type of field.
                                        loadedObject = mi.fieldIsStatic ?
                                            parentClass.staticFieldTypes[mi.fieldIndex] :
                                            parentClass.instanceFieldTypes[mi.fieldIndex];
                                        break;
                                    case MetaTokenType.mtArrayInitializer: // Get type of field.
                                        loadedObject = mi.arrayInitializerData;
                                        break;
                                    default: throw new Exception("Error: opcode 0xD0 called on token ID " + md.ToString("X8") + " Which is not currently handled.");
                                }

                                stackBuffer[++sp].LoadObject(loadedObject);

                                break; // ldtoken <token>
                            }

                        case 0xfe: // Extended opcodes
                            b = byteCode[pc++];
                            switch (b)
                            {
                                case 0x01:
                                case 0x02:
                                case 0x03:
                                case 0x04:
                                case 0x05:
                                    {
                                        StackElement sb = stackBuffer[sp--];
                                        StackElement sa = stackBuffer[sp];
                                        StackType promoted = StackElement.StackTypeMaxPromote(sa.type, sb.type);
                                        switch (b)
                                        {
                                            case 0x01: // CEQ
                                                switch (promoted)
                                                {
                                                    case StackType.Boolean: stackBuffer[sp].LoadInt(sa.i == sb.i ? 1 : 0); break;
                                                    case StackType.Int: stackBuffer[sp].LoadInt(sa.i == sb.i ? 1 : 0); break;
                                                    case StackType.Uint: stackBuffer[sp].LoadInt(sa.i == sb.i ? 1 : 0); break;
                                                    case StackType.Long: stackBuffer[sp].LoadInt(sa.l == sb.l ? 1 : 0); break;
                                                    case StackType.Ulong: stackBuffer[sp].LoadInt(sa.l == sb.l ? 1 : 0); break;
                                                    case StackType.Float: stackBuffer[sp].LoadInt(sa.f == sb.f ? 1 : 0); break;
                                                    case StackType.Double: stackBuffer[sp].LoadInt(sa.d == sb.d ? 1 : 0); break;
                                                    case StackType.Object:
                                                        if (sa.type == StackType.Object && sb.type == StackType.Object)
                                                            stackBuffer[sp].LoadInt(sa.o == sb.o ? 1 : 0);
                                                        else
                                                            throw new Exception($"CEQ Unimplemented type promotion unequal {sa.type} != {sb.type}");
                                                        break;
                                                    default: throw new Exception($"CEQ Unimplemented type promotion ({promoted})");
                                                }
                                                break;
                                            case 0x02: // CGT
                                                switch (promoted)
                                                {
                                                    case StackType.Int: stackBuffer[sp].LoadInt(sa.i > sb.i ? 1 : 0); break;
                                                    case StackType.Uint: stackBuffer[sp].LoadInt(sa.i > sb.i ? 1 : 0); break;
                                                    case StackType.Long: stackBuffer[sp].LoadInt(sa.l > sb.l ? 1 : 0); break;
                                                    case StackType.Ulong: stackBuffer[sp].LoadInt(sa.l > sb.l ? 1 : 0); break;
                                                    case StackType.Float: stackBuffer[sp].LoadInt(sa.f > sb.f ? 1 : 0); break;
                                                    case StackType.Double: stackBuffer[sp].LoadInt(sa.d > sb.d ? 1 : 0); break;
                                                    default: throw new Exception($"CEQ Unimplemented type promotion ({promoted})");
                                                }
                                                break;
                                            case 0x03: // CGT.UN
                                                switch (promoted)
                                                {
                                                    case StackType.Int: stackBuffer[sp].LoadInt(sa.u > sb.u ? 1 : 0); break;
                                                    case StackType.Uint: stackBuffer[sp].LoadInt(sa.u > sb.u ? 1 : 0); break;
                                                    case StackType.Long: stackBuffer[sp].LoadInt(sa.e > sb.e ? 1 : 0); break;
                                                    case StackType.Ulong: stackBuffer[sp].LoadInt(sa.e > sb.e ? 1 : 0); break;
                                                    case StackType.Float: stackBuffer[sp].LoadInt(sa.f > sb.f ? 1 : 0); break;
                                                    case StackType.Double: stackBuffer[sp].LoadInt(sa.d > sb.d ? 1 : 0); break;
                                                    default: throw new Exception($"CEQ Unimplemented type promotion ({promoted})");
                                                }
                                                break;
                                            case 0x04: // CLT
                                                switch (promoted)
                                                {
                                                    case StackType.Int: stackBuffer[sp].LoadInt(sa.i < sb.i ? 1 : 0); break;
                                                    case StackType.Uint: stackBuffer[sp].LoadInt(sa.i < sb.i ? 1 : 0); break;
                                                    case StackType.Long: stackBuffer[sp].LoadInt(sa.l < sb.l ? 1 : 0); break;
                                                    case StackType.Ulong: stackBuffer[sp].LoadInt(sa.l < sb.l ? 1 : 0); break;
                                                    case StackType.Float: stackBuffer[sp].LoadInt(sa.f < sb.f ? 1 : 0); break;
                                                    case StackType.Double: stackBuffer[sp].LoadInt(sa.d < sb.d ? 1 : 0); break;
                                                    default: throw new Exception($"CEQ Unimplemented type promotion ({promoted})");
                                                }
                                                break;
                                            case 0x05: // CLT.UN
                                                switch (promoted)
                                                {
                                                    case StackType.Int: stackBuffer[sp].LoadInt(sa.u < sb.u ? 1 : 0); break;
                                                    case StackType.Uint: stackBuffer[sp].LoadInt(sa.u < sb.u ? 1 : 0); break;
                                                    case StackType.Long: stackBuffer[sp].LoadInt(sa.e < sb.e ? 1 : 0); break;
                                                    case StackType.Ulong: stackBuffer[sp].LoadInt(sa.e < sb.e ? 1 : 0); break;
                                                    case StackType.Float: stackBuffer[sp].LoadInt(sa.f < sb.f ? 1 : 0); break;
                                                    case StackType.Double: stackBuffer[sp].LoadInt(sa.d < sb.d ? 1 : 0); break;
                                                    default: throw new Exception($"CEQ Unimplemented type promotion ({promoted})");
                                                }
                                                break;
                                        }
                                        break;
                                    }
                                case 0x06: // ldftn <method>
                                    uint bc = BytecodeAsU32(ref pc);
                                    CilMetadataTokenInfo dt = box.metadatas[bc];
                                    // Right now, we don't have any way of generating references to functions outside this cilbox.
                                    if (dt.isNative)
                                        throw new Exception($"Cannot create references to functions outside this cilbox ({dt.Name})");
                                    stackBuffer[++sp].LoadObject(box.classesList[dt.interpretiveMethodClass].methods[dt.interpretiveMethod]);
                                    break;
                                case 0x16: // constrained.
                                           // handled by reflection so discard the type token
                                    BytecodeAsU32(ref pc);
                                    break;
                                default:
                                    throw new Exception($"Opcode 0xfe 0x{b.ToString("X2")} unimplemented");
                            }
                            break;

                        default: throw new Exception($"Opcode 0x{b.ToString("X2")} unimplemented @ {pc}");
                    }
#if PER_INSTRUCTION_PROFILING
spiperf.End();
#endif
                }
                while (cont);
            }
            catch (Exception e)
            {
                string fullError = $"Breakwarn: {e.ToString()} Class: {parentClass.className}, Function: {methodName}, Bytecode: {pc}";
                Debug.LogError(fullError);
                box.disabledReason = fullError;
                box.disabled = true;
                //box.InterpreterExit();
                throw;
            }
#if UNITY_EDITOR
            perfMarkerInterpret.End();
#endif

            //box.InterpreterExit();

            return (sp == -1) ? StackElement.nil : stackBuffer[sp--];
        }

        uint BytecodeAs16(ref int i)
        {
            return (uint)CilboxUtil.BytecodePullLiteral(byteCode, ref i, 2);
        }
        uint BytecodeAsU32(ref int i)
        {
            return (uint)CilboxUtil.BytecodePullLiteral(byteCode, ref i, 4);
        }
        int BytecodeAsI32(ref int i)
        {
            return (int)CilboxUtil.BytecodePullLiteral(byteCode, ref i, 4);
        }
        ulong BytecodeAs64(ref int i)
        {
            return CilboxUtil.BytecodePullLiteral(byteCode, ref i, 8);
        }

        private void HandleArithmeticOpCode(byte b, ref int sp, Span<StackElement> stackBuffer, ref int pc)
        {
            StackElement sb = stackBuffer[sp--];
            StackElement sa = stackBuffer[sp];
            StackType promoted = StackElement.StackTypeMaxPromote(sa.type, sb.type);

            switch (b - 0x58)
            {
                case 0: // Add
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadInt(sa.i + sb.i); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u + sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadLong(sa.l + sb.l); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e + sb.e); break;
                        case StackType.Float: stackBuffer[sp].LoadFloat(sa.f + sb.f); break;
                        case StackType.Double: stackBuffer[sp].LoadDouble(sa.d + sb.d); break;
                    }
                    break;
                case 1: // Sub
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadInt(sa.i - sb.i); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u - sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadLong(sa.l - sb.l); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e - sb.e); break;
                        case StackType.Float: stackBuffer[sp].LoadFloat(sa.f - sb.f); break;
                        case StackType.Double: stackBuffer[sp].LoadDouble(sa.d - sb.d); break;
                    }
                    break;
                case 2: // Mul
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadInt(sa.i * sb.i); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u * sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadLong(sa.l * sb.l); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e * sb.e); break;
                        case StackType.Float: stackBuffer[sp].LoadFloat(sa.f * sb.f); break;
                        case StackType.Double: stackBuffer[sp].LoadDouble(sa.d * sb.d); break;
                    }
                    break;
                case 3: // Div
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadInt(sa.i / sb.i); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u / sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadLong(sa.l / sb.l); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e / sb.e); break;
                        case StackType.Float: stackBuffer[sp].LoadFloat(sa.f / sb.f); break;
                        case StackType.Double: stackBuffer[sp].LoadDouble(sa.d / sb.d); break;
                    }
                    break;
                case 4: // Div.un
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadUint(sa.u / sb.u); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u / sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadUlong(sa.e / sb.e); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e / sb.e); break;
                        default: throw new Exception($"Unexpected div.un instruction behavior @ {pc}");
                    }
                    break;
                case 5: // rem
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadInt(sa.i % sb.i); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u % sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadLong(sa.l % sb.l); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e % sb.e); break;
                        default: throw new Exception($"Unexpected rem instruction behavior @ {pc}");
                    }
                    break;
                case 6: // rem.un
                    switch (promoted)
                    {
                        case StackType.Int: stackBuffer[sp].LoadUint(sa.u % sb.u); break;
                        case StackType.Uint: stackBuffer[sp].LoadUint(sa.u % sb.u); break;
                        case StackType.Long: stackBuffer[sp].LoadUlong(sa.e % sb.e); break;
                        case StackType.Ulong: stackBuffer[sp].LoadUlong(sa.e % sb.e); break;
                        default: throw new Exception($"Unexpected rem.un instruction behavior @ {pc}");
                    }
                    break;
                case 7: stackBuffer[sp].LoadUlongType(sa.e & sb.e, promoted); break; // and
                case 8: stackBuffer[sp].LoadUlongType(sa.e | sb.e, promoted); break; // or
                case 9: stackBuffer[sp].LoadUlongType(sa.e ^ sb.e, promoted); break; // xor
                case 10: stackBuffer[sp].LoadUlongType(sa.e << sb.i, promoted); break; // shl
                case 11: // shr
                    switch (sa.type)
                    {
                        case StackType.Sbyte: // TODO: Is this right? Do we consider all unsigned types signed?
                        case StackType.Byte:
                        case StackType.Short:
                        case StackType.Ushort:
                        case StackType.Int:
                        case StackType.Uint: stackBuffer[sp].LoadLongType(sa.i >> sb.i, promoted); break;
                        case StackType.Long:
                        case StackType.Ulong: stackBuffer[sp].LoadLongType(sa.l >> sb.i, promoted); break;
                    }
                    break;
                case 12: stackBuffer[sp].LoadUlongType(sa.e >> sb.i, promoted); break; // shr.un
            }
        }
    }
}
