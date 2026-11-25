package sample.service;

import org.junit.jupiter.api.Test;

import java.time.LocalDate;

import static org.junit.jupiter.api.Assertions.assertEquals;

class DueDatesTest {
    @Test
    void readsGeneratedDate() {
        var dates = new DueDates();
        var date = LocalDate.of(2024, 1, 1);
        var created = dates.create(date);
        assertEquals(date, created.getDate());
        assertEquals(date, dates.effective(created, dates.empty()));
    }
}
