package cqc;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.*;
import java.util.*;
import org.jacoco.core.internal.data.CRC64;
import org.jacoco.core.internal.instr.InstrSupport;

/** Finds a runtime class under an evaluated Maven output and caches its original bytecode identity. */
final class CoverageClassFiles {
    record ClassFile(Path path, long id) {
        byte[] checkedBytes() throws IOException {
            var bytes = readBytes(path);
            if (id != CRC64.classId(bytes)) throw new IOException("Compiled class changed while reading coverage: " + path);
            return bytes;
        }
    }

    private record Lookup(String directory, String name) { }
    private final Map<Lookup, Optional<ClassFile>> files = new HashMap<>();

    Optional<ClassFile> find(String directory, String name) {
        if (directory == null) return Optional.empty();
        return files.computeIfAbsent(new Lookup(directory, name), CoverageClassFiles::inspect);
    }

    private static Optional<ClassFile> inspect(Lookup lookup) {
        try {
            return inspect(lookup.directory(), lookup.name());
        } catch (IOException exception) {
            throw new UncheckedIOException("Cannot inspect " + lookup.directory() + ": " + exception.getMessage(), exception);
        }
    }

    private static Optional<ClassFile> inspect(String directory, String name) throws IOException {
        var root = Path.of(directory).toAbsolutePath().normalize();
        var path = root.resolve(name + ".class").normalize();
        if (!path.startsWith(root)) throw new IOException("Class location escapes the compiled output.");
        if (Files.notExists(path, LinkOption.NOFOLLOW_LINKS)) return Optional.empty();
        var real = path.toRealPath();
        if (!real.startsWith(root.toRealPath())) throw new IOException("Class location escapes the compiled output.");
        var bytes = readBytes(real);
        if (!name.equals(InstrSupport.classReaderFor(bytes).getClassName()))
            throw new IOException("Bytecode declares a different binary class.");
        return Optional.of(new ClassFile(real, CRC64.classId(bytes)));
    }

    private static byte[] readBytes(Path path) throws IOException {
        if (!Files.isRegularFile(path, LinkOption.NOFOLLOW_LINKS)) throw new IOException("Compiled class is no longer a regular file: " + path);
        return Files.readAllBytes(path);
    }
}
