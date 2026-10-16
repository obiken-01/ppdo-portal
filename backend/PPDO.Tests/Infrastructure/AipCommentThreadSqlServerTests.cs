using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// AIP comment threads on a <b>real SQL Server</b> (v1.9.0 — PPDO-223, spec §5 and §11): the self FK
/// <c>parent_comment_id</c> (NO ACTION) against every path that deletes comments, and the open count's
/// head filter. Throwaway database built from the EF model (<see cref="SqlServerTestDatabase"/>); skipped
/// where there is no SQL Server, as on CI.
///
/// <para>
/// What deletes comments today, and why replies follow: comments are anchored by <c>node_type</c> +
/// <c>node_id</c>, not an FK, so deleting an activity, project or program (PPDO-88,
/// <c>AipService.DeleteNodeAsync</c>) reads its comments with <c>GetByNodesAsync</c> and removes them with
/// one <c>DeleteByIdsAsync</c>. A reply copies its head's node and group row, so it is in that read and
/// that one DELETE. Deleting an office group (or a whole record) cascades from <c>aip_offices</c>, again
/// one statement for head and replies. Nothing else deletes a comment.
/// </para>
///
/// <para>
/// ⚠️ <b>Red-tested (PPDO-223).</b> Dropping <c>ParentCommentId == null</c> from
/// <c>CountUnresolvedBySideAsync</c> fails <see cref="CountUnresolvedBySide_IgnoresReplies"/>.
/// <see cref="DeletingAHeadAlone_IsRefusedByTheSelfForeignKey"/> shows the FK is there and would refuse a
/// delete path that took the head without its replies.
/// </para>
/// </summary>
public sealed class AipCommentThreadSqlServerTests : IClassFixture<SqlServerTestDatabase>
{
    private const int ActivityId = 980;
    private readonly SqlServerTestDatabase _db;

    public AipCommentThreadSqlServerTests(SqlServerTestDatabase db) => _db = db;

    [SqlServerFact]
    public async Task CountUnresolvedBySide_IgnoresReplies()
    {
        (int groupId, Guid user) = await SeedOfficeAsync();
        int head = await AddAsync(groupId, user, AipCommentSide.Ppdo);
        await AddAsync(groupId, user, AipCommentSide.Encoder, parent: head);
        await AddAsync(groupId, user, AipCommentSide.DepartmentHead, parent: head);
        await AddAsync(groupId, user, AipCommentSide.Ppdo, parent: head);

        await using AppDbContext context = _db.NewContext();
        IReadOnlyDictionary<AipCommentSide, int> open =
            await new AipReviewCommentRepository(context).CountUnresolvedBySideAsync([groupId]);

        Assert.Equal(1, open.GetValueOrDefault(AipCommentSide.Ppdo));
        Assert.False(open.ContainsKey(AipCommentSide.DepartmentHead));
        Assert.False(open.ContainsKey(AipCommentSide.Encoder));
    }

    /// <summary>The node delete's two calls (PPDO-88), exactly as <c>AipService.DeleteNodeAsync</c> makes them.</summary>
    [SqlServerFact]
    public async Task NodeDelete_RemovesTheHeadAndItsRepliesInOneStatement()
    {
        (int groupId, Guid user) = await SeedOfficeAsync();
        // Its own node id: the read is by anchor across offices, and the other tests share this database.
        const int node = 77_001;
        int head = await AddAsync(groupId, user, AipCommentSide.Ppdo, nodeId: node);
        await AddAsync(groupId, user, AipCommentSide.Encoder, parent: head, nodeId: node);
        await AddAsync(groupId, user, AipCommentSide.Ppdo, parent: head, nodeId: node);
        int otherNode = await AddAsync(groupId, user, AipCommentSide.Ppdo, nodeId: node + 1);

        await using (AppDbContext context = _db.NewContext())
        {
            AipReviewCommentRepository repo = new(context);
            IReadOnlyList<AipReviewComment> onNode = await repo.GetByNodesAsync([], [], [node]);
            Assert.Equal(3, onNode.Count);   // the head and both replies, found by the anchor they share

            Assert.Equal(3, await repo.DeleteByIdsAsync(onNode.Select(c => c.Id).ToList()));
        }

        await using AppDbContext check = _db.NewContext();
        Assert.Equal([otherNode], await check.AipReviewComments.Where(c => c.AipOfficeId == groupId).Select(c => c.Id).ToListAsync());
    }

    [SqlServerFact]
    public async Task OfficeGroupDelete_CascadesToTheHeadAndItsReplies()
    {
        (int groupId, Guid user) = await SeedOfficeAsync();
        int head = await AddAsync(groupId, user, AipCommentSide.DepartmentHead);
        await AddAsync(groupId, user, AipCommentSide.Encoder, parent: head);

        await using (AppDbContext context = _db.NewContext())
            await context.Set<AipOffice>().Where(o => o.Id == groupId).ExecuteDeleteAsync();

        await using AppDbContext check = _db.NewContext();
        Assert.False(await check.AipReviewComments.AnyAsync(c => c.AipOfficeId == groupId));
    }

    [SqlServerFact]
    public async Task RecordDelete_CascadesThroughTheGroupToTheThread()
    {
        (int groupId, Guid user) = await SeedOfficeAsync();
        int head = await AddAsync(groupId, user, AipCommentSide.Ppdo);
        await AddAsync(groupId, user, AipCommentSide.Encoder, parent: head);
        int recordId;
        await using (AppDbContext context = _db.NewContext())
            recordId = await context.Set<AipOffice>().Where(o => o.Id == groupId).Select(o => o.AipRecordId).SingleAsync();

        await using (AppDbContext context = _db.NewContext())
            await context.Set<AipRecord>().Where(r => r.Id == recordId).ExecuteDeleteAsync();

        await using AppDbContext check = _db.NewContext();
        Assert.False(await check.AipReviewComments.AnyAsync(c => c.AipOfficeId == groupId));
    }

    [SqlServerFact]
    public async Task DeletingAHeadAlone_IsRefusedByTheSelfForeignKey()
    {
        (int groupId, Guid user) = await SeedOfficeAsync();
        int head = await AddAsync(groupId, user, AipCommentSide.Ppdo);
        await AddAsync(groupId, user, AipCommentSide.Encoder, parent: head);

        await using AppDbContext context = _db.NewContext();
        SqlException refused = await Assert.ThrowsAsync<SqlException>(() =>
            new AipReviewCommentRepository(context).DeleteByIdsAsync([head]));
        Assert.Contains("FK_aip_review_comments_aip_review_comments_parent_comment_id", refused.Message);
    }

    // ── Seed ─────────────────────────────────────────────────────────────────

    /// <summary>A fresh record and office group per test, so tests share the database without sharing rows.</summary>
    private async Task<(int GroupId, Guid User)> SeedOfficeAsync()
    {
        await using AppDbContext context = _db.NewContext();
        // The model seeds the SuperAdmin; any user will do as the author.
        Guid user = await context.Users.Select(u => u.Id).FirstAsync();

        AipRecord record = new()
        {
            FiscalYear = 2028, EntrySource = "Manual", Status = "Draft",
            UploadedById = user, UploadedAt = DateTime.UtcNow,
        };
        AipOffice group = new()
        {
            AipRecord = record, RefCode = "1000-000-1-01-010", Name = "PTO", Sector = "GENERAL",
            WorkflowStatus = "SubmittedToPpdo",
        };
        context.Add(group);
        await context.SaveChangesAsync();
        return (group.Id, user);
    }

    private async Task<int> AddAsync(
        int groupId, Guid user, AipCommentSide side, int? parent = null, int nodeId = ActivityId)
    {
        await using AppDbContext context = _db.NewContext();
        AipReviewComment comment = new()
        {
            AipOfficeId = groupId, NodeType = AipCommentNodeType.Activity, NodeId = nodeId,
            ParentCommentId = parent, AuthorId = user, AuthorSide = side,
            Body = parent is null ? "Check the unit cost" : "Fixed", CreatedAt = DateTime.UtcNow,
        };
        context.AipReviewComments.Add(comment);
        await context.SaveChangesAsync();
        return comment.Id;
    }
}
