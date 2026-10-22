import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

// Reflection permits compilation with a local JDK older than the installed game's
// class version. Execution must use that game's bundled Java and original jar.
public class HarnessSmoke {
    public static void main(String[] args) throws Exception {
        Class<?> platformType = Class.forName("se.krka.kahlua.vm.Platform");
        Class<?> tableType = Class.forName("se.krka.kahlua.vm.KahluaTable");
        Class<?> platformClass = Class.forName("se.krka.kahlua.j2se.J2SEPlatform");
        Object platform = platformClass.getMethod("getInstance").invoke(null);
        Object env = platformClass.getMethod("newTable").invoke(platform);
        Method set = tableType.getMethod("rawset", Object.class, Object.class);
        Method get = tableType.getMethod("rawget", Object.class);
        set.invoke(env, "_G", env);
        for (String lib : new String[] { "BaseLib" })
            Class.forName("se.krka.kahlua.stdlib." + lib).getMethod("register", tableType).invoke(null, env);
        for (String lib : new String[] { "CoroutineLib", "TableLib", "StringLib" })
            Class.forName("se.krka.kahlua.stdlib." + lib).getMethod("register", platformType, tableType).invoke(null, platform, env);
        Class.forName("se.krka.kahlua.j2se.MathLib").getMethod("register", platformType, tableType).invoke(null, platform, env);
        Class<?> threadType = Class.forName("se.krka.kahlua.vm.KahluaThread");
        Object thread = threadType.getConstructor(platformType, tableType).newInstance(platform, env);
        // Installed Build 42 requires the event-host thread owner to be recorded.
        Field owner = threadType.getDeclaredField("debugOwnerThread");
        owner.setAccessible(true); owner.set(thread, Thread.currentThread());
        Method compile = Class.forName("se.krka.kahlua.luaj.compiler.LuaCompiler")
            .getMethod("loadstring", String.class, String.class, tableType);
        Method call = threadType.getMethod("pcall", Object.class, Object[].class);
        Class<?> javaFunction = Class.forName("se.krka.kahlua.vm.JavaFunction");
        Object nativeFailure = Proxy.newProxyInstance(javaFunction.getClassLoader(), new Class<?>[] { javaFunction },
            (proxy, method, values) -> { if (method.getName().equals("call")) throw new IllegalStateException("native smoke exception"); return "nativeFailure"; });
        set.invoke(env, "nativeFailure", nativeFailure);
        String bootstrap = Files.readString(Path.of(args[2])) + "\n";
        String source = bootstrap + Files.readString(Path.of(args[0])) + "\n" + Files.readString(Path.of(args[1]));
        Object chunk = compile.invoke(null, source, "HarnessSmoke", env);
        check(call.invoke(thread, chunk, new Object[0]), "load");
        Object pzt = get.invoke(env, "PZT");
        Object tick = get.invoke(pzt, "tick");
        int hostErrors = 0;
        for (int i = 0; i < 100 && !Boolean.TRUE.equals(get.invoke(pzt, "finished")); i++) {
            try {
                Object[] result = (Object[]) call.invoke(thread, tick, new Object[0]);
                if (!Boolean.TRUE.equals(result[0])) { hostErrors++; System.out.println("HOST ERROR: " + Arrays.toString(result)); }
            } catch (InvocationTargetException failure) {
                hostErrors++; System.out.println("HOST EXCEPTION: " + failure.getCause());
            }
        }
        if (!Boolean.TRUE.equals(get.invoke(pzt, "finished"))) throw new AssertionError("Harness did not finish in 100 ticks");
        check(call.invoke(thread, get.invoke(env, "verifySmoke"), new Object[0]), "verify");
        int expectedErrors = ((Number) get.invoke(env, "expectedHostErrors")).intValue();
        if (hostErrors != expectedErrors) throw new AssertionError("Expected " + expectedErrors + " host errors, got " + hostErrors);
        System.out.println("KAHLUA SMOKE PASS; host errors=" + hostErrors);
    }
    static void check(Object value, String phase) {
        Object[] results = (Object[]) value;
        if (!Boolean.TRUE.equals(results[0])) throw new AssertionError(phase + ": " + Arrays.toString(results));
    }
}
