using System;
using System.Runtime.InteropServices;

// Runs a managed exe inside the game's own Mono runtime, the same one Unity embeds.
static class Program
{
    const string mono = "mono-2.0-bdwgc.dll";
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [DllImport(mono)] static extern void mono_set_dirs([MarshalAs(UnmanagedType.LPUTF8Str)] string assembly_dir, [MarshalAs(UnmanagedType.LPUTF8Str)] string config_dir);
    [DllImport(mono)] static extern IntPtr mono_jit_init([MarshalAs(UnmanagedType.LPUTF8Str)] string file);
    [DllImport(mono)] static extern IntPtr mono_domain_assembly_open(IntPtr domain, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(mono)] static extern int mono_jit_exec(IntPtr domain, IntPtr assembly, int argc, IntPtr[] argv);
    [DllImport(mono)] static extern void mono_config_parse(IntPtr filename);
    [DllImport(mono)] static extern void mono_set_assemblies_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    static int Main(string[] args)
    {
        string game = args[0], exe = args[1];
        SetDllDirectory(game + "/MonoBleedingEdge/EmbedRuntime");
        mono_set_dirs(game + "/Parkitect_Data/Managed", game + "/MonoBleedingEdge/etc");
        mono_set_assemblies_path(game + "/Parkitect_Data/Managed");
        mono_config_parse(IntPtr.Zero);
        IntPtr domain = mono_jit_init(exe);
        IntPtr assembly = mono_domain_assembly_open(domain, exe);
        IntPtr[] argv = new IntPtr[args.Length - 1];
        for (int i = 1; i < args.Length; i++) argv[i - 1] = Marshal.StringToCoTaskMemUTF8(args[i]);
        return mono_jit_exec(domain, assembly, argv.Length, argv);
    }
}
