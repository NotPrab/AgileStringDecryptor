using System;
using System.Reflection;

namespace AgileStringDecryptor {
    public static class Utils {
        internal static bool IsDynamicMethod(MethodBase? methodBase) {
            if (methodBase == null)
                throw new ArgumentNullException(nameof(methodBase));

            try {
                var token = methodBase.MetadataToken;
            }
            catch (InvalidOperationException) {
                return true;
            }
            return false;

        }
    }
}