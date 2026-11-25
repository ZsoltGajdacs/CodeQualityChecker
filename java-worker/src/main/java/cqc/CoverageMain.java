package cqc;

import com.google.gson.Gson;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Map;

/** Reads a staged reactor's JaCoCo inputs and produces module-owned coverage overlays. */
public final class CoverageMain {
    record Owner(String module, String classes, boolean localReport, String report) { }
    record Input(String module, String data, String testClasses, List<String> visibleModules,
                 List<String> competingPaths, String agentVersion, Map<String, List<String>> productionPaths) { }
    record Request(List<Owner> owners, List<Input> inputs) { }
    record Diagnostic(String code, String message, String module) { }
    record Response(List<String> reports, List<Diagnostic> diagnostics) { }

    public static void main(String[] args) {
        var json = new Gson();
        try {
            var request = json.fromJson(new InputStreamReader(System.in, StandardCharsets.UTF_8), Request.class);
            System.out.print(json.toJson(new ReactorCoverage(request.owners()).merge(request.inputs())));
        } catch (RuntimeException exception) {
            System.err.println(exception.getClass().getSimpleName() + ": " + exception.getMessage());
            System.exit(1);
        }
    }

    private CoverageMain() { }
}
