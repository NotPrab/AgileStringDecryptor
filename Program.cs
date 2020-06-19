using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace AgileStringDecryptor {
    internal static class Program {
        private static Module _module;
        private static ModuleDefMD _moduleDefMd;
        private static int _amount;

        private static void Main(string[] args) {
            Console.Title = "Agile String Decryptor by wwh1004 | Version : 6.x";
            try {
                // Load the assembly
                _module = Assembly.LoadFile(Path.GetFullPath(args[0])).ManifestModule;
            }
            catch {
                Console.WriteLine("Drag n Drop !");
                Console.ReadLine();
            }

            _moduleDefMd = ModuleDefMD.Load(args[0], new ModuleCreationOptions {
                TryToLoadPdbFromDisk = false
            });
            AgileDynamicStringDecryption();
            SaveAs(Path.Combine(
                Path.GetDirectoryName(args[0]) ?? throw new InvalidOperationException("Failed to save this module !"),
                Path.GetFileNameWithoutExtension(args[0]) + "-StrDec" + Path.GetExtension(args[0])));
            _moduleDefMd.Dispose();
            Console.WriteLine("[?] Decrypted : {0} strings", _amount);
            Console.WriteLine("[+] Done !");
            Console.ReadLine();
        }

        private static void AgileDynamicStringDecryption() {
            // Find namspace empty with class "<AgileDotNetRT>"
            var agileDotNetRt =
                _moduleDefMd.Types.First(t => t.Namespace == string.Empty && t.Name == "<AgileDotNetRT>");
            // Find a method in the class that has only one parameter with the parameter type String and the return value type String
            var decryptionMethod = agileDotNetRt.Methods.First(m =>
                m.Parameters.Count == 1 && m.Parameters[0].Type.TypeName == "String" &&
                m.ReturnType.TypeName == "String");
            // Convert dnlib's MethodDef to MethodBase in .NET reflection
            var decryptor = _module.ResolveMethod(decryptionMethod.MDToken.ToInt32());
            //Looping through all methods in that type and checking if method have body (instructions)
            foreach (var method in _moduleDefMd.EnumerateAllMethodDefs().Where(x => x.HasBody)) {
                var instr = method.Body.Instructions;
                for (var i = 0; i < instr.Count; i++)
                    if (instr[i].OpCode == OpCodes.Call && instr[i].Operand == decryptionMethod &&
                        instr[i - 1].OpCode == OpCodes.Ldstr) {
                        instr[i].OpCode = OpCodes.Nop;
                        instr[i].Operand = null;
                        instr[i - 1].Operand = decryptor.Invoke(null, new[] {
                            instr[i - 1].Operand
                        });
                        _amount++;
                    }
            }

            // remove decryption method from the assembly
            _moduleDefMd.Types.Remove(decryptionMethod.DeclaringType);
            Console.WriteLine("[^] Removed junk : {0} class", decryptionMethod.DeclaringType);
        }

        private static void SaveAs(string filePath) {
            var opts = new ModuleWriterOptions(_moduleDefMd);
            opts.MetadataOptions.Flags |= MetadataFlags.PreserveAll | MetadataFlags.KeepOldMaxStack;
            opts.Logger = DummyLogger.NoThrowInstance; //this is just to prevent write methods from throwing error
            _moduleDefMd.Write(filePath, opts);
        }
    }

    internal static class ModuleDefExtensions {
        public static IEnumerable<MethodDef> EnumerateAllMethodDefs(this ModuleDefMD moduleDefMd) {
            var methodTableLength = moduleDefMd.TablesStream.MethodTable.Rows;
            for (uint rid = 1; rid <= methodTableLength; rid++) yield return moduleDefMd.ResolveMethod(rid);
        }
    }
}
// Ref : https://github.com/wwh1004/blog/