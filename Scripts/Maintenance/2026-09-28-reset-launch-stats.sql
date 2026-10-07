-- One-time launch reset. Required sqlcmd variable: ApplyReset=0 previews (ROLLBACK).
-- To apply: sqlcmd ... -v ApplyReset=1 -i this-file.sql
-- All original fact values are retained by soft deletion. No provider calls are made.
-- Reset marker is unique to this operation; later live stats are outside the fixed cutoff.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
DECLARE @Apply bit = $(ApplyReset);
DECLARE @Cutoff datetime2(7) = '2026-09-28T21:33:00';
DECLARE @Actor nvarchar(128) = 'maintenance:launch-stats:2026-09-28';
DECLARE @Now datetime2(7) = SYSUTCDATETIME();
DECLARE @Changes TABLE (RecordType nvarchar(128), Affected int);
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.AuditLogEntries WITH (UPDLOCK,HOLDLOCK)
           WHERE Action='LaunchStatsReset' AND ActorId=@Actor)
BEGIN
    ROLLBACK;
    PRINT 'Launch reset already applied; no changes.';
    RETURN;
END;
-- Fail closed if new match activity needs a fresh scope review.
IF EXISTS (SELECT 1 FROM dbo.Matches m JOIN dbo.Sessions s ON s.Id=m.SessionId
           WHERE m.IsDeleted=0 AND s.StartsAtUtc >= '2026-09-01T00:00:00')
    THROW 51000, 'New match activity found; review reset scope before applying.', 1;
UPDATE dbo.MatchEvents SET IsDeleted=1, UpdatedAt=@Now, UpdatedBy=@Actor WHERE IsDeleted=0 AND CreatedAt<=@Cutoff;
INSERT @Changes VALUES ('MatchEvents',@@ROWCOUNT);
UPDATE dbo.PlayerRatingVotes SET IsDeleted=1, UpdatedAt=@Now, UpdatedBy=@Actor WHERE IsDeleted=0 AND CreatedAt<=@Cutoff;
INSERT @Changes VALUES ('PlayerRatingVotes',@@ROWCOUNT);
UPDATE dbo.PlayerLikes SET IsDeleted=1, UpdatedAt=@Now, UpdatedBy=@Actor WHERE IsDeleted=0 AND CreatedAt<=@Cutoff;
INSERT @Changes VALUES ('PlayerLikes',@@ROWCOUNT);
UPDATE dbo.MatchAwards SET IsDeleted=1, UpdatedAt=@Now, UpdatedBy=@Actor WHERE IsDeleted=0 AND CreatedAt<=@Cutoff;
INSERT @Changes VALUES ('MatchAwards',@@ROWCOUNT);
UPDATE dbo.PlayerMatchStats SET IsDeleted=1, UpdatedAt=@Now, UpdatedBy=@Actor WHERE IsDeleted=0 AND CreatedAt<=@Cutoff;
INSERT @Changes VALUES ('PlayerMatchStats',@@ROWCOUNT);
UPDATE dbo.MatchResults SET IsDeleted=1, UpdatedAt=@Now, UpdatedBy=@Actor WHERE IsDeleted=0 AND CreatedAt<=@Cutoff;
INSERT @Changes VALUES ('MatchResults',@@ROWCOUNT);
IF EXISTS (SELECT 1 FROM @Changes c JOIN (VALUES
    ('MatchEvents',13),('PlayerRatingVotes',151),('PlayerLikes',10),
    ('MatchAwards',3),('PlayerMatchStats',122),('MatchResults',17)
    ) expected(RecordType,Affected) ON expected.RecordType=c.RecordType
    WHERE c.Affected<>expected.Affected)
    THROW 51001, 'Stat counts differ from reviewed launch snapshot; no reset committed.', 1;
DECLARE @Details nvarchar(4000) = (SELECT RecordType,Affected FROM @Changes FOR JSON PATH);
INSERT dbo.AuditLogEntries
    (Id,CreatedAt,CreatedBy,IsDeleted,ActorType,ActorId,Action,EntityName,OccurredAtUtc,DetailsJson)
VALUES
    (NEWID(),@Now,@Actor,0,'System',@Actor,'LaunchStatsReset','PlayerStats',@Now,@Details);
SELECT RecordType,Affected FROM @Changes;
IF @Apply=1
BEGIN
    COMMIT;
    PRINT 'Launch reset committed.';
END
ELSE
BEGIN
    ROLLBACK;
    PRINT 'Preview only; all changes rolled back.';
END;
