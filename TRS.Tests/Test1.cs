using Microsoft.EntityFrameworkCore;
using TRS2._0.Services;
using TRS2._0.Models.DataModels;
using Moq;
using Microsoft.Extensions.Logging;

namespace TRS.Tests
{
    [TestClass]
    public class OverloadCorrectionTests
    {
        private TRSDBContext _context;
        private WorkCalendarService _service;

        [TestInitialize]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<TRSDBContext>()
                .UseInMemoryDatabase("TRSTestDB")
                .Options;

            _context = new TRSDBContext(options);

            var mockLogger = new Mock<ILogger<WorkCalendarService>>();
            _service = new WorkCalendarService(_context, mockLogger.Object);
        }

        // ✅ CASO 1: Una persona con overload y un viaje que requiere esfuerzo mínimo. El test valida que se preserve el mínimo del viaje y se reduzca el resto.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldRespectTravelEffortAndReduceOthers()
        {
            int personId = 1;
            int projId = 1001;
            int wpTravel = 2001;
            int wpOther = 2002;
            int wpxTravel = 3001;
            int wpxOther = 3002;
            var month = new DateTime(2025, 1, 1);

            _context.Personnel.Add(new Personnel
            {
                Id = personId,
                Name = "Test",
                Surname = "User",
                Email = "test@trs.local",
                Password = "hashed-or-placeholder"
            });

            _context.Projects.Add(new Project
            {
                ProjId = projId,
                Start = month,
                EndReportDate = month.AddMonths(12),
                Type = "HORIZON",
                SType = "Research",
                St1 = "Open",
                St2 = "Execution"
            });

            _context.Wps.AddRange(
                new Wp { Id = wpTravel, ProjId = projId, Name = "WP-Travel", StartDate = month, EndDate = month.AddMonths(6) },
                new Wp { Id = wpOther, ProjId = projId, Name = "WP-Other", StartDate = month, EndDate = month.AddMonths(6) }
            );

            _context.Wpxpeople.AddRange(
                new Wpxperson { Id = wpxTravel, Person = personId, Wp = wpTravel },
                new Wpxperson { Id = wpxOther, Person = personId, Wp = wpOther }
            );

            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 0.8m });

            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = wpxTravel, Month = month, Value = 0.5m },
                new Perseffort { WpxPerson = wpxOther, Month = month, Value = 0.5m }
            );

            _context.Liquidations.Add(new Liquidation
            {
                Id = "LIQ001",
                PersId = personId,
                Start = month.AddDays(10),
                End = month.AddDays(12),
                Destiny = "Barcelona",
                Project1 = "TRS-Test",
                Reason = "Test trip",
                Status = "Approved"
            });

            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ001", ProjId = projId, Day = month.AddDays(10), PersId = personId, PMs = 0.15m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ001", ProjId = projId, Day = month.AddDays(11), PersId = personId, PMs = 0.15m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ001", ProjId = projId, Day = month.AddDays(12), PersId = personId, PMs = 0.10m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 1);

            Assert.IsTrue(result.Success, "Expected overload correction to succeed.");

            var updatedEfforts = await _context.Persefforts.ToListAsync();
            var total = updatedEfforts.Sum(e => e.Value);
            var travelEffort = updatedEfforts.FirstOrDefault(e => e.WpxPerson == wpxTravel)?.Value;
            var otherEffort = updatedEfforts.FirstOrDefault(e => e.WpxPerson == wpxOther)?.Value;

            Console.WriteLine($"Travel effort result: {travelEffort}");
            Console.WriteLine($"Other effort result: {otherEffort}");

            Assert.IsTrue(Math.Round(total, 2) <= 0.8m, "Total effort should not exceed PM limit.");
            Assert.IsTrue(travelEffort >= 0.4m, "Travel effort should be preserved.");
            Assert.IsTrue(otherEffort <= 0.4m, "Other effort should be reduced to avoid overload.");
        }

        // ✅ CASO 2: Todos los proyectos están bloqueados. La función no debe aplicar ningún cambio y debe devolver un error controlado.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldFailWhenAllProjectsAreLocked()
        {
            int personId = 2;
            int projId = 2001;
            int wp = 2101;
            int wpx = 2201;
            var month = new DateTime(2025, 2, 1);

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Locked", Surname = "User", Email = "locked@trs.local", Password = "123" });
            _context.Projects.Add(new Project { ProjId = projId, Start = month, EndReportDate = month.AddMonths(6), Type = "TYPE", SType = "STYPE", St1 = "S1", St2 = "S2" });
            _context.Wps.Add(new Wp { Id = wp, ProjId = projId, Name = "LockedWP", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wpxpeople.Add(new Wpxperson { Id = wpx, Person = personId, Wp = wp });
            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 0.5m });
            _context.Persefforts.Add(new Perseffort { WpxPerson = wpx, Month = month, Value = 0.8m });
            _context.ProjectMonthLocks.Add(new ProjectMonthLock { PersonId = personId, ProjectId = projId, Year = 2025, Month = 2, IsLocked = true });

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 2);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Locked efforts exceed available PM. Overload cannot be resolved.", result.Message);
        }

        // ✅ CASO 3: El viaje necesita más effort del disponible. El sistema debe abortar el ajuste.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldFailWhenTravelExceedsAvailablePM()
        {
            int personId = 3;
            int projId = 3001;
            int wp = 3101;
            int wpx = 3201;
            var month = new DateTime(2025, 3, 1);

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Over", Surname = "Travel", Email = "travel@trs.local", Password = "123" });
            _context.Projects.Add(new Project { ProjId = projId, Start = month, EndReportDate = month.AddMonths(6), Type = "T", SType = "S", St1 = "A", St2 = "B" });
            _context.Wps.Add(new Wp { Id = wp, ProjId = projId, Name = "WP", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wpxpeople.Add(new Wpxperson { Id = wpx, Person = personId, Wp = wp });
            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 0.4m });
            _context.Persefforts.Add(new Perseffort { WpxPerson = wpx, Month = month, Value = 0.5m });
            _context.Liquidations.Add(new Liquidation { Id = "LIQ002", PersId = personId, Start = month, End = month.AddDays(3), Destiny = "Roma", Project1 = "PRJ", Reason = "Conf", Status = "Done" });
            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ002", ProjId = projId, Day = month, PersId = personId, PMs = 0.2m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ002", ProjId = projId, Day = month.AddDays(1), PersId = personId, PMs = 0.2m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ002", ProjId = projId, Day = month.AddDays(2), PersId = personId, PMs = 0.2m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 3);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Available PM is not enough to justify travel-related efforts.", result.Message);
        }

        // ✅ CASO 4: No hay overload. El sistema debe detectar que no es necesario ajustar.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldSkipWhenNoOverload()
        {
            int personId = 4;
            int projId = 4001;
            int wp = 4101;
            int wpx = 4201;
            var month = new DateTime(2025, 4, 1);

            _context.Personnel.Add(new Personnel { Id = personId, Name = "No", Surname = "Overload", Email = "no@trs.local", Password = "123" });
            _context.Projects.Add(new Project { ProjId = projId, Start = month, EndReportDate = month.AddMonths(6), Type = "X", SType = "Y", St1 = "Z", St2 = "Q" });
            _context.Wps.Add(new Wp { Id = wp, ProjId = projId, Name = "WP", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wpxpeople.Add(new Wpxperson { Id = wpx, Person = personId, Wp = wp });
            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 1.0m });
            _context.Persefforts.Add(new Perseffort { WpxPerson = wpx, Month = month, Value = 0.9m });

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 4);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("No overload found.", result.Message);
        }

        // ✅ CASO 5: Un único WP sin viajes debe ser reducido parcialmente para igualar el PM disponible (reducción parcial).
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldFullyReduceSingleNonTravelWP()
        {
            int personId = 5;
            int projId = 5001;
            int wp = 5101;
            int wpx = 5201;
            var month = new DateTime(2025, 5, 1);

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Solo", Surname = "WP", Email = "single@trs.local", Password = "123" });
            _context.Projects.Add(new Project { ProjId = projId, Start = month, EndReportDate = month.AddMonths(6), Type = "S", SType = "T", St1 = "U", St2 = "V" });
            _context.Wps.Add(new Wp { Id = wp, ProjId = projId, Name = "WP", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wpxpeople.Add(new Wpxperson { Id = wpx, Person = personId, Wp = wp });
            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 0.5m });
            _context.Persefforts.Add(new Perseffort { WpxPerson = wpx, Month = month, Value = 0.6m });

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 5);
            Assert.IsTrue(result.Success);

            var updated = await _context.Persefforts.FirstOrDefaultAsync(e => e.WpxPerson == wpx);
            Assert.AreEqual(0.5m, Math.Round(updated?.Value ?? -1, 2), "WP should be reduced to match available PM");
        }
            

        // ✅ CASO 6: Un único WP sin viajes debe ser completamente reducido si no hay PM disponible.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldReduceToZeroWhenNoAvailablePM()
        {
            int personId = 6;
            int projId = 6001;
            int wp = 6101;
            int wpx = 6201;
            var month = new DateTime(2025, 6, 1);

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Zero", Surname = "Effort", Email = "zero@trs.local", Password = "123" });
            _context.Projects.Add(new Project { ProjId = projId, Start = month, EndReportDate = month.AddMonths(6), Type = "T1", SType = "T2", St1 = "T3", St2 = "T4" });
            _context.Wps.Add(new Wp { Id = wp, ProjId = projId, Name = "WP", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wpxpeople.Add(new Wpxperson { Id = wpx, Person = personId, Wp = wp });
            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 0.0m });
            _context.Persefforts.Add(new Perseffort { WpxPerson = wpx, Month = month, Value = 0.6m });

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 6);
            Assert.IsTrue(result.Success);

            var updated = await _context.Persefforts.FirstOrDefaultAsync(e => e.WpxPerson == wpx);
            Assert.AreEqual(0.0m, Math.Round(updated?.Value ?? -1, 2), "WP should be reduced to zero due to no PM available");
        }

        // ✅ CASO 7: Varios proyectos con viajes. Uno requiere ajuste individual, los demás se ajustan con el ratio global. El total se ajusta como máximo al PM disponible (1.0).
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldHandleMixedTravelProjects()
        {
            int personId = 7;
            var month = new DateTime(2025, 7, 1);

            // Proyectos y WPs
            int proj1 = 7001, proj2 = 7002, proj3 = 7003;
            int wp1 = 7101, wp2 = 7102, wp3 = 7103, wp4 = 7104;
            int wpx1 = 7201, wpx2 = 7202, wpx3 = 7203, wpx4 = 7204;

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Mixed", Surname = "Travel", Email = "mixed@trs.local", Password = "123" });

            _context.Projects.AddRange(
                new Project { ProjId = proj1, Start = month, EndReportDate = month.AddMonths(6), Type = "A", SType = "B", St1 = "C", St2 = "D" },
                new Project { ProjId = proj2, Start = month, EndReportDate = month.AddMonths(6), Type = "A", SType = "B", St1 = "C", St2 = "D" },
                new Project { ProjId = proj3, Start = month, EndReportDate = month.AddMonths(6), Type = "A", SType = "B", St1 = "C", St2 = "D" }
            );

            _context.Wps.AddRange(
                new Wp { Id = wp1, ProjId = proj1, Name = "WP1", StartDate = month, EndDate = month.AddMonths(6) },
                new Wp { Id = wp2, ProjId = proj2, Name = "WP2", StartDate = month, EndDate = month.AddMonths(6) },
                new Wp { Id = wp3, ProjId = proj3, Name = "WP3", StartDate = month, EndDate = month.AddMonths(6) },
                new Wp { Id = wp4, ProjId = proj3, Name = "WP4", StartDate = month, EndDate = month.AddMonths(6) }
            );

            _context.Wpxpeople.AddRange(
                new Wpxperson { Id = wpx1, Person = personId, Wp = wp1 },
                new Wpxperson { Id = wpx2, Person = personId, Wp = wp2 },
                new Wpxperson { Id = wpx3, Person = personId, Wp = wp3 },
                new Wpxperson { Id = wpx4, Person = personId, Wp = wp4 }
            );

            // PM permitido
            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 1.0m });

            // Effort actual total = 1.6
            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = wpx1, Month = month, Value = 0.6m }, // Proj1 → requiere ajuste (viaje mínimo 0.5)
                new Perseffort { WpxPerson = wpx2, Month = month, Value = 0.4m },
                new Perseffort { WpxPerson = wpx3, Month = month, Value = 0.3m },
                new Perseffort { WpxPerson = wpx4, Month = month, Value = 0.3m }
            );

            // Viajes
            _context.Liquidations.Add(new Liquidation { Id = "LIQ007", PersId = personId, Start = month.AddDays(5), End = month.AddDays(6), Destiny = "Berlin", Project1 = "P1", Reason = "Test", Status = "Approved" });
            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ007", ProjId = proj1, Day = month.AddDays(5), PersId = personId, PMs = 0.5m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ007", ProjId = proj2, Day = month.AddDays(6), PersId = personId, PMs = 0.2m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 7);
            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");

            var efforts = await _context.Persefforts.ToListAsync();
            var totalEffort = efforts.Sum(e => e.Value);

            Console.WriteLine($"Effort Total: {totalEffort}");
            foreach (var e in efforts)
                Console.WriteLine($"WpxPerson {e.WpxPerson} → {e.Value}");

            Console.WriteLine($"Available PM: 1.0 | Total after adjustment: {totalEffort}");
            Assert.AreEqual(1.0m, Math.Round(totalEffort, 2), "Adjusted total effort should match available PM.");
            Assert.IsTrue(efforts.First(e => e.WpxPerson == wpx1).Value >= 0.5m, "Proj1 must keep at least 0.5 for travel.");
            Assert.IsTrue(efforts.First(e => e.WpxPerson == wpx2).Value >= 0.2m, "Proj2 must keep at least 0.2 for travel.");
        }

        // ✅ CASO 8: Múltiples WPs en varios proyectos, algunos con viajes, otros no.
        // Debe ajustar globalmente manteniendo los mínimos de viaje y reduciendo el resto a 1.0.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldDistributeAcrossMultipleProjects()
        {
            int personId = 8;
            var month = new DateTime(2025, 8, 1);

            int[] projIds = { 8001, 8002, 8003 };
            int[] wpIds = { 8101, 8102, 8103, 8104, 8105, 8106 };
            int[] wpxIds = { 8201, 8202, 8203, 8204, 8205, 8206 };

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Multi", Surname = "Project", Email = "multi@trs.local", Password = "123" });

            for (int i = 0; i < 3; i++)
            {
                _context.Projects.Add(new Project { ProjId = projIds[i], Start = month, EndReportDate = month.AddMonths(6), Type = "R", SType = "T", St1 = "A", St2 = "B" });
            }

            for (int i = 0; i < 6; i++)
            {
                _context.Wps.Add(new Wp { Id = wpIds[i], ProjId = projIds[i / 2], Name = $"WP-{i + 1}", StartDate = month, EndDate = month.AddMonths(6) });
                _context.Wpxpeople.Add(new Wpxperson { Id = wpxIds[i], Person = personId, Wp = wpIds[i] });
            }

            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 1.0m });

            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = wpxIds[0], Month = month, Value = 0.3m },
                new Perseffort { WpxPerson = wpxIds[1], Month = month, Value = 0.3m },
                new Perseffort { WpxPerson = wpxIds[2], Month = month, Value = 0.3m },
                new Perseffort { WpxPerson = wpxIds[3], Month = month, Value = 0.3m },
                new Perseffort { WpxPerson = wpxIds[4], Month = month, Value = 0.2m },
                new Perseffort { WpxPerson = wpxIds[5], Month = month, Value = 0.2m }
            );

            _context.Liquidations.Add(new Liquidation { Id = "LIQ008", PersId = personId, Start = month.AddDays(2), End = month.AddDays(4), Destiny = "Paris", Project1 = "ProjTravel", Reason = "Workshop", Status = "Approved" });

            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ008", ProjId = projIds[0], Day = month.AddDays(2), PersId = personId, PMs = 0.2m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ008", ProjId = projIds[0], Day = month.AddDays(3), PersId = personId, PMs = 0.1m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 8);
            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");

            var efforts = await _context.Persefforts.ToListAsync();
            var totalEffort = efforts.Sum(e => e.Value);

            Console.WriteLine($"Effort Total: {totalEffort}");
            foreach (var e in efforts)
                Console.WriteLine($"WpxPerson {e.WpxPerson} → {e.Value}");

            Assert.AreEqual(1.0m, Math.Round(totalEffort, 2), "Adjusted total effort should equal PM");

            var travelEffort = efforts.Where(e => e.WpxPerson == wpxIds[0] || e.WpxPerson == wpxIds[1]).Sum(e => e.Value);
            Assert.IsTrue(travelEffort >= 0.3m, "Travel-related efforts should be preserved");
        }

        // ✅ CASO 9: Siete WPs en varios proyectos, con dos viajes. El overload total es 1.2 y debe ser ajustado.
        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldDistributeEffortWithSmallOverloadAndTwoTravels()
        {
            int personId = 9;
            var month = new DateTime(2025, 9, 1);

            int[] projIds = { 9001, 9002, 9003 };
            int[] wpIds = { 9101, 9102, 9103, 9104, 9105, 9106, 9107 };
            int[] wpxIds = { 9201, 9202, 9203, 9204, 9205, 9206, 9207 };

            _context.Personnel.Add(new Personnel
            {
                Id = personId,
                Name = "Overload",
                Surname = "Traveler",
                Email = "over@trs.local",
                Password = "123"
            });

            for (int i = 0; i < 3; i++)
            {
                _context.Projects.Add(new Project
                {
                    ProjId = projIds[i],
                    Start = month,
                    EndReportDate = month.AddMonths(6),
                    Type = "R",
                    SType = "T",
                    St1 = "A",
                    St2 = "B"
                });
            }

            for (int i = 0; i < 7; i++)
            {
                _context.Wps.Add(new Wp
                {
                    Id = wpIds[i],
                    ProjId = projIds[i / 3],
                    Name = $"WP-{i + 1}",
                    StartDate = month,
                    EndDate = month.AddMonths(6)
                });

                _context.Wpxpeople.Add(new Wpxperson
                {
                    Id = wpxIds[i],
                    Person = personId,
                    Wp = wpIds[i]
                });
            }

            _context.PersMonthEfforts.Add(new PersMonthEffort
            {
                PersonId = personId,
                Month = month,
                Value = 1.0m
            });

            // Total effort = 1.2
            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = wpxIds[0], Month = month, Value = 0.2m }, // Proj 1
                new Perseffort { WpxPerson = wpxIds[1], Month = month, Value = 0.2m }, // Proj 1
                new Perseffort { WpxPerson = wpxIds[2], Month = month, Value = 0.2m }, // Proj 2
                new Perseffort { WpxPerson = wpxIds[3], Month = month, Value = 0.2m }, // Proj 2
                new Perseffort { WpxPerson = wpxIds[4], Month = month, Value = 0.2m }, // Proj 3
                new Perseffort { WpxPerson = wpxIds[5], Month = month, Value = 0.1m }, // Proj 3
                new Perseffort { WpxPerson = wpxIds[6], Month = month, Value = 0.1m }  // Proj 3
            );

            // Agregar dos viajes para Proj1 y Proj2
            _context.Liquidations.Add(new Liquidation
            {
                Id = "LIQ009A",
                PersId = personId,
                Start = month.AddDays(3),
                End = month.AddDays(4),
                Destiny = "Brussels",
                Project1 = "TRS",
                Reason = "Conf",
                Status = "Approved"
            });

            _context.Liquidations.Add(new Liquidation
            {
                Id = "LIQ009B",
                PersId = personId,
                Start = month.AddDays(10),
                End = month.AddDays(11),
                Destiny = "Lisbon",
                Project1 = "TRS",
                Reason = "Meeting",
                Status = "Approved"
            });

            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ009A", ProjId = projIds[0], Day = month.AddDays(3), PersId = personId, PMs = 0.2m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ009B", ProjId = projIds[1], Day = month.AddDays(10), PersId = personId, PMs = 0.15m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 9);
            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");

            var efforts = await _context.Persefforts.Where(e => e.Month == month && wpxIds.Contains(e.WpxPerson)).ToListAsync();
            var total = efforts.Sum(e => e.Value);

            Console.WriteLine($"Adjusted total effort: {total}");
            foreach (var e in efforts)
                Console.WriteLine($"WpxPerson {e.WpxPerson} → {e.Value}");

            Assert.AreEqual(1.0m, Math.Round(total, 2), "Total effort should match PM");
            Assert.IsTrue(efforts.Where(e => e.WpxPerson == wpxIds[0] || e.WpxPerson == wpxIds[1]).Sum(e => e.Value) >= 0.2m, "Travel-related effort for Proj1 should be preserved");
            Assert.IsTrue(efforts.Where(e => e.WpxPerson == wpxIds[2] || e.WpxPerson == wpxIds[3]).Sum(e => e.Value) >= 0.15m, "Travel-related effort for Proj2 should be preserved");
        }


        // ✅ CASO 10: Múltiples WPs en varios proyectos con viajes variados.
        // Incluye tres proyectos con viajes: dos de ellos requieren ratios personalizados porque su mínimo no se adapta al ratio global,
        // y uno sí se adapta. Además, hay varios WPs sin viajes. El sistema debe aplicar ajustes proporcionales personalizados donde sea necesario,
        // usar el ratio global donde sea viable y reducir el resto para no superar el PM permitido (0.80), partiendo de un overload total de 1.30.

        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldApplyCustomAndGlobalRatiosToMixedTravelProjects()
        {
            int personId = 10;
            var month = new DateTime(2025, 10, 1);

            int[] projIds = { 10001, 10002, 10003, 10004, 10005, 10006 };
            int[] wpIds = { 10101, 10102, 10103, 10104, 10105, 10106, 10107, 10108, 10109 };
            int[] wpxIds = { 10201, 10202, 10203, 10204, 10205, 10206, 10207, 10208, 10209 };

            _context.Personnel.Add(new Personnel
            {
                Id = personId,
                Name = "Test",
                Surname = "Case10",
                Email = "case10@trs.local",
                Password = "123"
            });

            for (int i = 0; i < projIds.Length; i++)
            {
                _context.Projects.Add(new Project
                {
                    ProjId = projIds[i],
                    Start = month,
                    EndReportDate = month.AddMonths(6),
                    Type = "R",
                    SType = "T",
                    St1 = "A",
                    St2 = "B"
                });
            }

            for (int i = 0; i < wpIds.Length; i++)
            {
                _context.Wps.Add(new Wp
                {
                    Id = wpIds[i],
                    ProjId = projIds[i / 2],
                    Name = $"WP-{i + 1}",
                    StartDate = month,
                    EndDate = month.AddMonths(6)
                });

                _context.Wpxpeople.Add(new Wpxperson
                {
                    Id = wpxIds[i],
                    Person = personId,
                    Wp = wpIds[i]
                });
            }

            _context.PersMonthEfforts.Add(new PersMonthEffort
            {
                PersonId = personId,
                Month = month,
                Value = 0.80m
            });

            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = wpxIds[0], Month = month, Value = 0.30m }, // P1 - viaje fuerte
                new Perseffort { WpxPerson = wpxIds[1], Month = month, Value = 0.30m }, // P2 - viaje fuerte
                new Perseffort { WpxPerson = wpxIds[2], Month = month, Value = 0.15m }, // P3 - viaje débil
                new Perseffort { WpxPerson = wpxIds[3], Month = month, Value = 0.15m }, // P4
                new Perseffort { WpxPerson = wpxIds[4], Month = month, Value = 0.10m }, // P4
                new Perseffort { WpxPerson = wpxIds[5], Month = month, Value = 0.10m }, // P5
                new Perseffort { WpxPerson = wpxIds[6], Month = month, Value = 0.05m }, // P5
                new Perseffort { WpxPerson = wpxIds[7], Month = month, Value = 0.10m }, // P6
                new Perseffort { WpxPerson = wpxIds[8], Month = month, Value = 0.05m }  // P6
            );

            // Liquidaciones
            _context.Liquidations.AddRange(
                new Liquidation { Id = "LIQ010A", PersId = personId, Start = month.AddDays(5), End = month.AddDays(5), Destiny = "Madrid", Project1 = "P1", Reason = "Conf", Status = "Approved" },
                new Liquidation { Id = "LIQ010B", PersId = personId, Start = month.AddDays(10), End = month.AddDays(10), Destiny = "Rome", Project1 = "P2", Reason = "Research", Status = "Approved" },
                new Liquidation { Id = "LIQ010C", PersId = personId, Start = month.AddDays(15), End = month.AddDays(15), Destiny = "London", Project1 = "P3", Reason = "Workshop", Status = "Approved" }
            );

            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ010A", ProjId = projIds[0], Day = month.AddDays(5), PersId = personId, PMs = 0.25m, Status = "Confirmed" }, // P1
                new Liqdayxproject { LiqId = "LIQ010B", ProjId = projIds[1], Day = month.AddDays(10), PersId = personId, PMs = 0.28m, Status = "Confirmed" }, // P2
                new Liqdayxproject { LiqId = "LIQ010C", ProjId = projIds[2], Day = month.AddDays(15), PersId = personId, PMs = 0.10m, Status = "Confirmed" }  // P3
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 10);
            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");

            var efforts = await _context.Persefforts
                            .Where(e => e.Month == month && wpxIds.Contains(e.WpxPerson))
                            .ToListAsync();

            var total = efforts.Sum(e => e.Value);
            Console.WriteLine($"Adjusted total effort: {total}");
            foreach (var e in efforts.OrderBy(e => e.WpxPerson))
                Console.WriteLine($"WpxPerson {e.WpxPerson} → {e.Value}");

            Assert.AreEqual(0.80m, Math.Round(total, 2), "Total effort should equal PM limit");

            var projEfforts = new Dictionary<int, decimal>
            {
                [projIds[0]] = efforts.Where(e => e.WpxPerson == wpxIds[0] || e.WpxPerson == wpxIds[1]).Sum(e => e.Value),
                [projIds[1]] = efforts.Where(e => e.WpxPerson == wpxIds[2] || e.WpxPerson == wpxIds[3]).Sum(e => e.Value),
                [projIds[2]] = efforts.Where(e => e.WpxPerson == wpxIds[4] || e.WpxPerson == wpxIds[5]).Sum(e => e.Value)
            };

            Assert.IsTrue(projEfforts[projIds[0]] >= 0.25m, $"Proj1 must preserve minimum required travel effort (got {projEfforts[projIds[0]]})");
            Assert.IsTrue(projEfforts[projIds[1]] >= 0.28m, $"Proj2 must preserve minimum required travel effort (got {projEfforts[projIds[1]]})");
            Assert.IsTrue(projEfforts[projIds[2]] >= 0.10m, $"Proj3 must preserve minimum required travel effort (got {projEfforts[projIds[2]]})");


        }

        // ✅ CASO 11: Validación con esfuerzos muy pequeños y redondeo en overload mínimo
        // El objetivo es verificar cómo se comporta el sistema cuando los efforts son cercanos a cero
        // y el overload es mínimo (0.02). Se espera que los efforts se ajusten sin caer por debajo
        // de 0.01, que se respete el mínimo del viaje (0.07) y que el total ajustado final sea 0.13.

        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldHandleTinyEffortsAndMinimalOverload()
        {
            int personId = 11;
            var month = new DateTime(2025, 11, 1);

            int[] projIds = { 11001, 11002 };
            int[] wpIds = { 11101, 11102, 11103, 11104, 11105 };
            int[] wpxIds = { 11201, 11202, 11203, 11204, 11205 };

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Tiny", Surname = "Efforts", Email = "tiny@trs.local", Password = "123" });

            _context.Projects.Add(new Project { ProjId = projIds[0], Start = month, EndReportDate = month.AddMonths(6), Type = "R", SType = "T", St1 = "A", St2 = "B" });
            _context.Projects.Add(new Project { ProjId = projIds[1], Start = month, EndReportDate = month.AddMonths(6), Type = "R", SType = "T", St1 = "A", St2 = "B" });

            _context.Wps.Add(new Wp { Id = 11101, ProjId = 11001, Name = $"WP-1", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wps.Add(new Wp { Id = 11102, ProjId = 11001, Name = $"WP-2", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wps.Add(new Wp { Id = 11103, ProjId = 11002, Name = $"WP-3", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wps.Add(new Wp { Id = 11104, ProjId = 11002, Name = $"WP-4", StartDate = month, EndDate = month.AddMonths(6) });
            _context.Wps.Add(new Wp { Id = 11105, ProjId = 11002, Name = $"WP-5", StartDate = month, EndDate = month.AddMonths(6) });

            _context.Wpxpeople.Add(new Wpxperson { Id = 11201, Person = personId, Wp = 11101 });
            _context.Wpxpeople.Add(new Wpxperson { Id = 11202, Person = personId, Wp = 11102 });
            _context.Wpxpeople.Add(new Wpxperson { Id = 11203, Person = personId, Wp = 11103 });
            _context.Wpxpeople.Add(new Wpxperson { Id = 11204, Person = personId, Wp = 11104 });
            _context.Wpxpeople.Add(new Wpxperson { Id = 11205, Person = personId, Wp = 11105 });

            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 0.13m });

            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = 11201, Month = month, Value = 0.02m },
                new Perseffort { WpxPerson = 11202, Month = month, Value = 0.05m },
                new Perseffort { WpxPerson = 11203, Month = month, Value = 0.03m },
                new Perseffort { WpxPerson = 11204, Month = month, Value = 0.03m },
                new Perseffort { WpxPerson = 11205, Month = month, Value = 0.02m }
            );

            _context.Liquidations.Add(new Liquidation
            {
                Id = "LIQ011",
                PersId = personId,
                Start = month.AddDays(3),
                End = month.AddDays(4),
                Destiny = "Zurich",
                Project1 = "P1",
                Reason = "MiniConf",
                Status = "Approved"
            });

            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ011", ProjId = 11001, Day = month.AddDays(3), PersId = personId, PMs = 0.04m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ011", ProjId = 11001, Day = month.AddDays(4), PersId = personId, PMs = 0.03m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 11);
            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");

            var efforts = await _context.Persefforts.ToListAsync();
            var totalEffort = efforts.Sum(e => e.Value);
            Console.WriteLine($"Effort Total: {totalEffort}");
            foreach (var e in efforts.OrderBy(e => e.WpxPerson))
                Console.WriteLine($"WpxPerson {e.WpxPerson} → {e.Value}");

            Assert.AreEqual(0.13m, Math.Round(totalEffort, 2), "Adjusted total effort should equal PM");

            var travelEffort = efforts.Where(e => e.WpxPerson == 11201 || e.WpxPerson == 11202).Sum(e => e.Value);
            Assert.IsTrue(travelEffort >= 0.07m, $"Travel-related efforts should be preserved (got {travelEffort})");
        }


        // ✅ CASO 12: Reducción forzada por exceso de viajes bloqueados.
        // Se simula una situación donde tres proyectos tienen viajes asociados y uno de ellos consume el 100% del PM permitido,
        // dejando a los otros proyectos con viajes por debajo del mínimo. El sistema debe preservar completamente el esfuerzo del
        // proyecto más prioritario (por ejemplo, por ser bloqueado) y eliminar por completo el esfuerzo de otros proyectos con viajes 
        // que no alcanzan su mínimo, ya que no se pueden justificar. Se valida que los esfuerzos se reduzcan a 0 de forma controlada 
        // sin romper el equilibrio total ni comprometer la justificación mínima de ningún viaje confirmado.


        [TestMethod]
        public async Task AdjustMonthlyOverloadAsync_ShouldPrioritizeTravelProjectsOverOthers()
        {
            int personId = 12;
            var month = new DateTime(2025, 12, 1);

            int[] projIds = { 12001, 12002, 12003 };
            int[] wpIds = { 12101, 12102, 12103, 12104, 12105, 12106 };
            int[] wpxIds = { 12201, 12202, 12203, 12204, 12205, 12206 };

            _context.Personnel.Add(new Personnel { Id = personId, Name = "Travel", Surname = "Priority", Email = "travel@trs.local", Password = "123" });

            for (int i = 0; i < 3; i++)
            {
                _context.Projects.Add(new Project
                {
                    ProjId = projIds[i],
                    Start = month,
                    EndReportDate = month.AddMonths(6),
                    Type = "R",
                    SType = "T",
                    St1 = "A",
                    St2 = "B"
                });
            }

            for (int i = 0; i < 6; i++)
            {
                _context.Wps.Add(new Wp { Id = wpIds[i], ProjId = projIds[i / 2], Name = $"WP-{i + 1}", StartDate = month, EndDate = month.AddMonths(6) });
                _context.Wpxpeople.Add(new Wpxperson { Id = wpxIds[i], Person = personId, Wp = wpIds[i] });
            }

            _context.PersMonthEfforts.Add(new PersMonthEffort { PersonId = personId, Month = month, Value = 1.0m });

            _context.Persefforts.AddRange(
                new Perseffort { WpxPerson = wpxIds[0], Month = month, Value = 0.15m }, // P1
                new Perseffort { WpxPerson = wpxIds[1], Month = month, Value = 0.15m }, // P1
                new Perseffort { WpxPerson = wpxIds[2], Month = month, Value = 0.15m }, // P2
                new Perseffort { WpxPerson = wpxIds[3], Month = month, Value = 0.15m }, // P2
                new Perseffort { WpxPerson = wpxIds[4], Month = month, Value = 0.4m },  // P3 (sin viaje)
                new Perseffort { WpxPerson = wpxIds[5], Month = month, Value = 0.4m }   // P3 (sin viaje)
            );

            _context.Liquidations.AddRange(
                new Liquidation { Id = "LIQ012A", PersId = personId, Start = month.AddDays(5), End = month.AddDays(6), Destiny = "Berlin", Project1 = "P1", Reason = "Visit", Status = "Approved" },
                new Liquidation { Id = "LIQ012B", PersId = personId, Start = month.AddDays(15), End = month.AddDays(16), Destiny = "Oslo", Project1 = "P2", Reason = "Training", Status = "Approved" }
            );

            _context.liqdayxproject.AddRange(
                new Liqdayxproject { LiqId = "LIQ012A", ProjId = projIds[0], Day = month.AddDays(5), PersId = personId, PMs = 0.25m, Status = "Confirmed" },
                new Liqdayxproject { LiqId = "LIQ012B", ProjId = projIds[1], Day = month.AddDays(15), PersId = personId, PMs = 0.30m, Status = "Confirmed" }
            );

            await _context.SaveChangesAsync();

            var result = await _service.AdjustMonthlyOverloadAsync(personId, 2025, 12);
            Assert.IsTrue(result.Success, $"Expected success but got: {result.Message}");

            var efforts = await _context.Persefforts.ToListAsync();
            var totalEffort = efforts.Sum(e => e.Value);

            Console.WriteLine($"Effort Total: {totalEffort}");
            foreach (var e in efforts.OrderBy(e => e.WpxPerson))
                Console.WriteLine($"WpxPerson {e.WpxPerson} → {e.Value}");

            Assert.AreEqual(1.0m, Math.Round(totalEffort, 2), "Adjusted total effort should equal PM");

            var travelEffort = efforts
                .Where(e => e.WpxPerson == wpxIds[0] || e.WpxPerson == wpxIds[1] || e.WpxPerson == wpxIds[2] || e.WpxPerson == wpxIds[3])
                .Sum(e => e.Value);

            Assert.IsTrue(travelEffort >= 0.55m, "Travel-related efforts should be preserved");

            var nonTravelEffort = efforts
                .Where(e => e.WpxPerson == wpxIds[4] || e.WpxPerson == wpxIds[5])
                .Sum(e => e.Value);

            Assert.IsTrue(nonTravelEffort <= 0.45m, "Non-travel efforts should be reduced if needed");
        }


    }

}
