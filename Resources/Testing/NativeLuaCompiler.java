import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.lang.reflect.*;

// Compiled with javac --release 8. Executed by the selected game's JVM and jar.
// Compilation only: the returned Lua closure is never called.
public class NativeLuaCompiler {
    static String encode(String text) { return Base64.getEncoder().encodeToString(text.getBytes(StandardCharsets.UTF_8)); }
    public static void main(String[] files) throws Exception {
        Class<?> compiler = Class.forName("se.krka.kahlua.luaj.compiler.LuaCompiler");
        Method compile = compiler.getMethod("loadstring", String.class, String.class, Class.forName("se.krka.kahlua.vm.KahluaTable"));
        boolean failed = false;
        for (String file : files) {
            try {
                String source = new String(Files.readAllBytes(Paths.get(file)), StandardCharsets.UTF_8);
                compile.invoke(null, source, file, null);
                System.out.println("PZTC\t" + encode(file) + "\tpassed\t");
            } catch (Throwable ex) {
                failed = true;
                Throwable error = ex instanceof InvocationTargetException ? ex.getCause() : ex;
                System.out.println("PZTC\t" + encode(file) + "\tfailed\t" + encode(error.toString()));
            }
        }
        if (failed) System.exit(1);
    }
}
