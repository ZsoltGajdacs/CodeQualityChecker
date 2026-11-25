package sample.service;

import java.util.List;

public class JvmAliases<T extends Number> {
    @SafeVarargs
    public final <U extends T, V extends List<String>> int measure(V labels, U[] previous, U... samples) {
        return labels.size() + previous.length + samples.length;
    }

    public enum Mode {
        SHORT(7), LONG("long", 1, 5);

        private final int value;

        Mode(int value) {
            this.value = value;
        }

        Mode(String label, int order, int value) {
            this.value = label.length() + order + value;
        }

        public int value() {
            return value;
        }
    }

    public class Inner {
        private final int value;

        public Inner(int value) {
            this.value = value;
        }

        public int value() {
            return value;
        }
    }

    public static class Nested {
        private final int value;

        public Nested(JvmAliases<?> owner, int value) {
            this.value = value;
        }

        public int value() {
            return value;
        }
    }
}
