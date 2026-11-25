package sample.api;

import lombok.Builder;
import lombok.Getter;

import java.time.LocalDate;

@Builder
@Getter
public class DueDate {
    private final LocalDate date;
}
