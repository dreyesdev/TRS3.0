select * from AffCodifications
select * from personnel where id= 335;
select count(persid) from dedication
select count(persid) from Dedications_backup 


INSERT INTO dedication (PersId, Reduc, Start, [End], Type, Exist, LineId)
SELECT PersId, Reduc, Start, [End], Type, 1, NULL
FROM Dedications_backup
WHERE Type > 0;

ALTER TABLE dedication
ALTER COLUMN LineId INT NULL;
