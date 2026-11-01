using System.Runtime.InteropServices;

/// <summary>
/// Never called. Declaring one native import per GL function-pointer signature that Silk.NET uses makes the wasm build generate
/// the managed-to-native call wrappers for those shapes; without them the runtime aborts on the first call with that signature.
/// </summary>
static class GlSignatures
{
    [DllImport("prism_gl")] static extern int prism_sig_i_();
    [DllImport("prism_gl")] static extern int prism_sig_i_i(int a0);
    [DllImport("prism_gl")] static extern int prism_sig_i_ii(int a0, int a1);
    [DllImport("prism_gl")] static extern int prism_sig_i_iii(int a0, int a1, int a2);
    [DllImport("prism_gl")] static extern void prism_sig_v_ff(float a0, float a1);
    [DllImport("prism_gl")] static extern void prism_sig_v_ffff(float a0, float a1, float a2, float a3);
    [DllImport("prism_gl")] static extern void prism_sig_v_i(int a0);
    [DllImport("prism_gl")] static extern void prism_sig_v_idd(int a0, double a1, double a2);
    [DllImport("prism_gl")] static extern void prism_sig_v_idddd(int a0, double a1, double a2, double a3, double a4);
    [DllImport("prism_gl")] static extern void prism_sig_v_iff(int a0, float a1, float a2);
    [DllImport("prism_gl")] static extern void prism_sig_v_iffff(int a0, float a1, float a2, float a3, float a4);
    [DllImport("prism_gl")] static extern void prism_sig_v_ii(int a0, int a1);
    [DllImport("prism_gl")] static extern void prism_sig_v_iif(int a0, int a1, float a2);
    [DllImport("prism_gl")] static extern void prism_sig_v_iii(int a0, int a1, int a2);
    [DllImport("prism_gl")] static extern void prism_sig_v_iiii(int a0, int a1, int a2, int a3);
    [DllImport("prism_gl")] static extern void prism_sig_v_iiiii(int a0, int a1, int a2, int a3, int a4);
    [DllImport("prism_gl")] static extern void prism_sig_v_iiiiii(int a0, int a1, int a2, int a3, int a4, int a5);
    [DllImport("prism_gl")] static extern void prism_sig_v_iiiiiiiii(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8);
    [DllImport("prism_gl")] static extern void prism_sig_v_iiiiiiiiii(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9);
}
