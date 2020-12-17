using System;
using System.IO;
using System.Linq;
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace AgileStringDecryptor {
    internal static class Program {
        private static Module? _module;
        private static ModuleDefMD? _moduleDefMd;
        private static int _stringsAmount;
        private static int _callsAmount;

        internal static void Main(string[] args) {
            Console.Title = "Agile String Decryptor by wwh1004 | Version : 6.x";
            try {
                _module = Assembly.LoadFile(Path.GetFullPath(args[0])).ManifestModule;
            }
            catch {
                Console.WriteLine("Drag n Drop !");
                Console.ReadLine();
            }

            _moduleDefMd = ModuleDefMD.Load(args[0], new ModuleCreationOptions {
                TryToLoadPdbFromDisk = false
            });
            Action decryptString = DecryptString;
            decryptString();
            Action fixProxyCall = FixProxyCall;
            fixProxyCall();
            SaveAs(Path.Combine(
                Path.GetDirectoryName(args[0]) ?? throw new InvalidOperationException("Failed to save this module !"),
                Path.GetFileNameWithoutExtension(args[0]) + "-StrDec" + Path.GetExtension(args[0])));
            _moduleDefMd.Dispose();
            Console.WriteLine($"[?] Fixed : {_callsAmount} calls");
            Console.WriteLine($"[?] Decrypted : {_stringsAmount} strings");
            Console.WriteLine("[+] Done !");
            Console.ReadLine();
        }

        internal static void FixProxyCall() {
            var globalTypes = _moduleDefMd?.Types.Where(t => t.Namespace == string.Empty).ToArray();
            var decryptor = (globalTypes ?? throw new InvalidOperationException()).Single(t => t.Name.StartsWith("{", StringComparison.Ordinal) && 
                                                    t.Name.EndsWith("}", StringComparison.Ordinal)).Methods.Single(m => !m.IsInstanceConstructor && m.Parameters.Count == 1);

            foreach (var typeDef in globalTypes) {
                var cctor = typeDef.FindStaticConstructor();
                if(cctor is null || !cctor.Body.Instructions.Any(i => i.OpCode == OpCodes.Call && i.Operand == decryptor))
                    continue;
                switch (_module) {
                    case not null: {
                        foreach (var fieldInfo in _module.ResolveType(typeDef.MDToken.ToInt32())
                            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)!) {
                            var proxyFieldToken = fieldInfo.MetadataToken;
                            var proxyFieldDef = _moduleDefMd?.ResolveField((uint) proxyFieldToken - 0x4000000);
                            var realMethod = ((Delegate) fieldInfo.GetValue(null)!).Method;

                            if (Utils.IsDynamicMethod(realMethod)) {
                                var dynamicMethodBodyReader = new DynamicMethodBodyReader(_moduleDefMd, realMethod);
                                dynamicMethodBodyReader.Read();
                                var instructionList = dynamicMethodBodyReader.GetMethod().Body.Instructions;
                                ReplaceAllOperand(proxyFieldDef, instructionList[instructionList.Count - 2].OpCode,
                                    (MemberRef) instructionList[instructionList.Count - 2].Operand);
                            }
                            else {
                                ReplaceAllOperand(proxyFieldDef, realMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call,
                                    (MemberRef) _moduleDefMd.Import(realMethod));
                            }
                        }

                        break;
                    }
                }
            }
        }

        internal static void ReplaceAllOperand(FieldDef? proxyFieldDef, OpCode callOrCallvirt, MemberRef realMethod) {
            if (proxyFieldDef is null) throw new ArgumentNullException(nameof(proxyFieldDef));
            if (realMethod is null) throw new ArgumentNullException(nameof(realMethod));

            if (_moduleDefMd == null) return;
            foreach (var method in _moduleDefMd.EnumerateAllMethodDefs()) {
                if (!method.HasBody) continue;
                var instr = method.Body.Instructions;
                for (var i = 0; i < instr.Count; i++) {
                    if (instr[i].OpCode != OpCodes.Ldsfld || instr[i].Operand != proxyFieldDef) continue;
                    for (var j = i; j < instr.Count; j++) {
                        if (instr[j].OpCode.Code != Code.Call || !(instr[j].Operand is MethodDef) ||
                            ((MethodDef) instr[j].Operand).DeclaringType !=
                            ((TypeDefOrRefSig) proxyFieldDef.FieldType).TypeDefOrRef) continue;
                        instr[i].OpCode = OpCodes.Nop;
                        instr[i].Operand = null;
                        instr[j].OpCode = callOrCallvirt;
                        instr[j].Operand = realMethod;
                        _callsAmount++;
                        break;
                    }
                }
            }
        }


        internal static void DecryptString() {
            // Find namespace empty with class "<AgileDotNetRT>"
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
                        // The instruction corresponding to [i-1] is ldstr, 
                        // we call the string decryptor method and replace the decrypted string back
                        _stringsAmount++;
                    }
            }

            // remove decryption method from the assembly
            _moduleDefMd.Types.Remove(decryptionMethod.DeclaringType);
        }

        internal static void SaveAs(string filePath) {
            var opts = new ModuleWriterOptions(_moduleDefMd) {
                MetadataOptions = {Flags = MetadataFlags.PreserveAll}, Logger = DummyLogger.NoThrowInstance
            };
            _moduleDefMd.Write(filePath, opts);
        }
    }
}
// Ref : https://github.com/wwh1004/blog/