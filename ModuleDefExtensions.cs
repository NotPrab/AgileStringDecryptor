using System.Collections.Generic;
using dnlib.DotNet;

namespace AgileStringDecryptor {
    internal static class ModuleDefExtensions {
        public static IEnumerable<MethodDef> EnumerateAllMethodDefs(this ModuleDefMD moduleDefMd) {
            var methodTableLength = moduleDefMd.TablesStream.MethodTable.Rows;
            // Get the length of the Method table. 
            for (uint rid = 1; rid <= methodTableLength; rid++) yield return moduleDefMd.ResolveMethod(rid);
        }
    }
}