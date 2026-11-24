package cqc;

import com.google.gson.annotations.SerializedName;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.util.*;
import java.util.jar.JarFile;

/** An existing Maven classpath entry, read as bytecode without loading its classes. */
record ClasspathArtifact(Path path, Kind kind) {
    enum Kind {
        @SerializedName("jar") JAR,
        @SerializedName("directory") DIRECTORY
    }

    record ClassDigest(String name, byte[] sha256) {
        boolean canConflict() { return !name.equals("module-info.class") && !name.endsWith("/module-info.class"); }
    }

    List<ClassDigest> verifiedClasses(String expectedHash) throws IOException {
        if (kind == Kind.DIRECTORY) {
            if (!Files.isDirectory(path, LinkOption.NOFOLLOW_LINKS)) throw missing();
            var classes = directoryClasses();
            verify(directoryHash(classes), expectedHash);
            return classes;
        }

        if (!Files.isRegularFile(path, LinkOption.NOFOLLOW_LINKS)) throw missing();
        if (!path.getFileName().toString().endsWith(".jar")) throw new IOException("Artifact must be a JAR: " + path.getFileName());
        try (var bytes = Files.newInputStream(path)) { verify(hash(bytes), expectedHash); }
        return jarClasses();
    }

    private IOException missing() { return new IOException("Missing artifact " + path.getFileName()); }

    private void verify(byte[] actualHash, String expectedHash) throws IOException {
        if (!HexFormat.of().formatHex(actualHash).equals(expectedHash))
            throw new IOException("Changed artifact " + path.getFileName());
    }

    private List<ClassDigest> directoryClasses() throws IOException {
        var classes = new TreeMap<String, ClassDigest>();
        try (var files = Files.walk(path)) {
            for (var iterator = files.iterator(); iterator.hasNext();) {
                var file = iterator.next();
                if (Files.isSymbolicLink(file)) throw new IOException("Classpath output contains a symbolic link: " + file);
                if (!Files.isRegularFile(file) || !file.getFileName().toString().endsWith(".class")) continue;
                String name = path.relativize(file).toString().replace(file.getFileSystem().getSeparator(), "/");
                try (var bytes = Files.newInputStream(file)) { classes.put(name, new ClassDigest(name, hash(bytes))); }
            }
        } catch (UncheckedIOException exception) { throw exception.getCause(); }
        return List.copyOf(classes.values());
    }

    private List<ClassDigest> jarClasses() throws IOException {
        var classes = new ArrayList<ClassDigest>();
        try (var jar = new JarFile(path.toFile())) {
            for (var entry : Collections.list(jar.entries())) {
                if (entry.isDirectory() || !entry.getName().endsWith(".class")) continue;
                try (var bytes = jar.getInputStream(entry)) { classes.add(new ClassDigest(entry.getName(), hash(bytes))); }
            }
        }
        return classes;
    }

    private static byte[] directoryHash(List<ClassDigest> classes) {
        var digest = sha256();
        // Server: sorted UTF-8 relative name, NUL, then the raw 32-byte file digest.
        for (var entry : classes) {
            digest.update(entry.name().getBytes(StandardCharsets.UTF_8));
            digest.update((byte)0);
            digest.update(entry.sha256());
        }
        return digest.digest();
    }

    private static byte[] hash(InputStream stream) throws IOException {
        var digest = sha256();
        byte[] bytes = new byte[8192];
        int count;
        while ((count = stream.read(bytes)) != -1) digest.update(bytes, 0, count);
        return digest.digest();
    }

    private static MessageDigest sha256() {
        try { return MessageDigest.getInstance("SHA-256"); }
        catch (NoSuchAlgorithmException exception) { throw new IllegalStateException(exception); }
    }
}
