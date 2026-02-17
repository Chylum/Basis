using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Cilbox
{
    // This overrides System.Runtime.CompilerServices.RuntimeHelpers
    // WARNING: This class is 100% available from WITHIN cilbox.
    public class CilboxPublicUtils
    {
        public static void InitializeArray(Array arr, byte[] initializer)
        {
            if (initializer == null || arr == null)
                throw new Exception("Error, array or initializer are null");
            if (initializer.Length != System.Runtime.InteropServices.Marshal.SizeOf(arr.GetType().GetElementType()) * arr.Length)
                throw new Exception("InitializeArray requires identical array byte length " + initializer.Length);
            Buffer.BlockCopy(initializer, 0, arr, 0, initializer.Length);
        }

        public static String GetProxyInitialPath(MonoBehaviour m)
        {
            CilboxProxy p = (CilboxProxy)m;
            return p.initialLoadPath;
        }

        public static String GetProxyBuildTimeGuid(MonoBehaviour m)
        {
            CilboxProxy p = (CilboxProxy)m;
            return p.buildTimeGuid;
        }
    }
}
