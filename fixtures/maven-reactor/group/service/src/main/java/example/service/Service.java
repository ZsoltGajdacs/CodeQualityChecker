package example.service;
import example.api.Value;
public class Service {
    public int number() { return example.api.FixtureSupport.identity(new Value().number()); }
}
