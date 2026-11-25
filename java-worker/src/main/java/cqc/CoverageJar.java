package cqc;

import java.io.*;
import java.nio.file.Path;
import java.util.*;
import java.util.jar.JarFile;
import org.jacoco.core.data.ExecutionData;
import org.jacoco.core.internal.data.CRC64;

/** Indexes competing archive entries once, then reads and caches only relevant class identities. */
final class CoverageJar {
    private static final String VERSIONS = "META-INF/versions/";
    private final Path path;
    private final Set<String> entries = new HashSet<>();
    private final Map<String, List<String>> variants = new HashMap<>();
    private final Map<String, Long> baseIds = new HashMap<>();
    private final Map<String, Set<Long>> variantIds = new HashMap<>();

    CoverageJar(Path path) throws IOException {
        this.path = path;
        try (var jar = open()) {
            jar.stream().map(entry -> entry.getName()).filter(entry -> entry.endsWith(".class")).forEach(this::index);
        }
    }

    boolean matches(ExecutionData data, boolean includeInactiveVariants) {
        String name = data.getName() + ".class";
        if (entries.contains(name) && baseIds.computeIfAbsent(name, this::readBaseId) == data.getId()) return true;
        return includeInactiveVariants && variants.containsKey(name)
            && variantIds.computeIfAbsent(name, this::readVariantIds).contains(data.getId());
    }

    private void index(String entry) {
        if (!entry.startsWith(VERSIONS)) {
            entries.add(entry);
            return;
        }
        int separator = entry.indexOf('/', VERSIONS.length());
        if (separator >= 0)
            variants.computeIfAbsent(entry.substring(separator + 1), key -> new ArrayList<>()).add(entry);
    }

    private long readBaseId(String name) {
        try (var jar = open()) {
            return id(jar, name);
        } catch (IOException exception) {
            throw new UncheckedIOException(exception);
        }
    }

    private Set<Long> readVariantIds(String name) {
        try (var jar = open()) {
            var ids = new HashSet<Long>();
            for (var variant : variants.get(name)) ids.add(id(jar, variant));
            return ids;
        } catch (IOException exception) {
            throw new UncheckedIOException(exception);
        }
    }

    private JarFile open() throws IOException { return new JarFile(path.toFile(), false); }

    private static long id(JarFile jar, String name) throws IOException {
        var entry = jar.getJarEntry(name);
        if (entry == null) throw new IOException("Archive changed while reading " + name);
        try (var input = jar.getInputStream(entry)) {
            return CRC64.classId(input.readAllBytes());
        }
    }
}
