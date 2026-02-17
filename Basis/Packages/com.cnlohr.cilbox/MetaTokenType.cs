using System;
using System.Collections.Generic;
using System.Text;

namespace Cilbox
{
    public enum MetaTokenType
    {
        mtType = 1,
        mtField = 4,
        mtString = 0x70,
        mtMethod = 10,
        mtArrayInitializer = 13, // Made-up type. 13 is unused in HandleKind.
    }
}
