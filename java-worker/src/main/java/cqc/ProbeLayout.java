package cqc;

import org.jacoco.core.internal.analysis.ClassAnalyzer;
import org.jacoco.core.internal.analysis.ClassCoverageImpl;
import org.jacoco.core.internal.analysis.StringPool;
import org.jacoco.core.internal.flow.ClassProbesAdapter;
import org.jacoco.core.internal.instr.InstrSupport;
import org.objectweb.asm.ClassReader;

/** Counts the probes JaCoCo would assign to the original production bytecode. */
final class ProbeLayout extends ClassAnalyzer {
    private int count;

    private ProbeLayout(String name) {
        super(new ClassCoverageImpl(name, 0, false), null, new StringPool());
    }

    static int count(byte[] bytes) {
        var reader = InstrSupport.classReaderFor(bytes);
        var layout = new ProbeLayout(reader.getClassName());
        var needsFrames = InstrSupport.needsFrames(InstrSupport.getMajorVersion(reader));
        reader.accept(new ClassProbesAdapter(layout, needsFrames), ClassReader.EXPAND_FRAMES);
        return layout.count;
    }

    @Override public void visitTotalProbeCount(int total) {
        count = total;
    }
}
