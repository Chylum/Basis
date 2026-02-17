using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Cilbox
{
    public class CilboxExceptionHandlingClause
    {
        public ExceptionHandlingClauseOptions Flags;
        public int TryOffset;
        public int TryLength;
        public int TryEndOffset;
        public int HandlerOffset;
        public int HandlerLength;
        public Type? CatchType;
    }
}
