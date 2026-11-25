package sample.service;

import sample.api.DueDate;

import java.time.LocalDate;
import java.util.Optional;

public class DueDates {
    public DueDate create(LocalDate date) {
        return DueDate.builder().date(date).build();
    }

    public LocalDate effective(DueDate value, LocalDate fallback) {
        return Optional.ofNullable(value).map(DueDate::getDate).orElse(fallback);
    }

    public java.time.LocalDate empty() {
        return LocalDate.EPOCH;
    }
}
