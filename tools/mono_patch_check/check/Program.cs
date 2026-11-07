using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Loads the built mod next to the real game assemblies and checks every patch target and reflective accessor resolve.
static class Program
{
    static StreamWriter log;
    static void write(string text) { Console.WriteLine(text); log.WriteLine(text); log.Flush(); }

    static int Main(string[] args)
    {
        string managed = args[0], mod_path = args[1];
        log = new StreamWriter(args[2]);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string candidate = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        write("Runtime: " + (Type.GetType("Mono.Runtime") != null ? "Mono" : "CLR"));
        Assembly mod = Assembly.LoadFrom(mod_path);
        write("Exported types: " + string.Join(", ", mod.GetExportedTypes().Select(t => t.FullName).ToArray()));

        int failures = 0;
        var harmony = new Harmony("patch_check");
        foreach (Type type in AccessTools.GetTypesFromAssembly(mod))
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            write("patching " + type.Name);
            try
            {
                var methods = harmony.CreateClassProcessor(type).Patch();
                write("PATCHED " + type.Name + " -> " + string.Join(", ", methods.Select(m => m.DeclaringType + "." + m.Name).ToArray()));
            }
            catch (Exception e) { failures++; write("FAILED  " + type.Name + ": " + e.GetBaseException().Message); }
        }
        foreach (Type type in AccessTools.GetTypesFromAssembly(mod))
        {
            if (type.IsGenericTypeDefinition) continue;
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.IsInitOnly && !field.IsLiteral && field.GetValue(null) == null) { failures++; write("NULL ACCESSOR " + type.Name + "." + field.Name); }
                }
            }
            catch (Exception e) { failures++; write("ACCESSORS FAILED " + type.Name + ": " + e.GetBaseException().Message); }
        }
        write(failures == 0 ? "ALL OK" : failures + " FAILURES");
        return failures;
    }
}
