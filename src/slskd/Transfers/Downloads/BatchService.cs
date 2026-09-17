// <copyright file="BatchService.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

namespace slskd.Transfers.Downloads;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

/// <summary>
///     Manages transfer batches.
/// </summary>
public interface IBatchService
{
    /// <summary>
    ///     Creates a new batch record.
    /// </summary>
    /// <param name="batch">The batch to create.</param>
    /// <returns>The created batch.</returns>
    Task<Batch> CreateAsync(Batch batch);

    /// <summary>
    ///     Finds a single batch matching the specified <paramref name="expression"/>.
    /// </summary>
    /// <param name="expression">The expression to use to match batches.</param>
    /// <returns>The found batch, or default if not found.</returns>
    /// <exception cref="ArgumentException">Thrown when an expression is not supplied.</exception>
    Task<Batch> FindAsync(Expression<Func<Batch, bool>> expression);
}

/// <summary>
///     Manages transfer batches.
/// </summary>
public class BatchService : IBatchService
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="BatchService"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory to use.</param>
    /// <param name="searchService">The search service, for labelling a batch with its search's text.</param>
    public BatchService(
        IDbContextFactory<TransfersDbContext> contextFactory,
        slskd.Search.ISearchService searchService)
    {
        ContextFactory = contextFactory;
        Searches = searchService;
    }

    private IDbContextFactory<TransfersDbContext> ContextFactory { get; }
    private slskd.Search.ISearchService Searches { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<BatchService>();

    /// <summary>
    ///     Creates a new batch record.
    /// </summary>
    /// <param name="batch">The batch to create.</param>
    /// <returns>The created batch.</returns>
    public async Task<Batch> CreateAsync(Batch batch)
    {
        if (batch == default)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        if (batch.Id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(batch.Id), message: $"Batch ID may not be an empty uuid ({Guid.Empty})");
        }

        /*
            stamp the search's text on the batch, here rather than at each caller: the label has to be taken at
            the moment of the enqueue to be worth recording at all -- resolving it later through SearchId gives
            nothing once the search has been deleted or pruned, which is exactly when a download's origin is
            worth asking about.

            a caller may supply its own text, and one that does is left alone. a search that cannot be found
            leaves it null; the batch is still created, because failing an enqueue over a missing label would
            be refusing the download for the sake of the annotation on it.
        */
        if (batch.SearchId.HasValue && string.IsNullOrEmpty(batch.SearchText))
        {
            try
            {
                var search = await Searches.FindAsync(s => s.Id == batch.SearchId.Value);

                batch = batch with { SearchText = search?.SearchText };
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to resolve the text of search {SearchId} for batch {BatchId}: {Message}", batch.SearchId, batch.Id, ex.Message);
            }
        }

        try
        {
            using var context = ContextFactory.CreateDbContext();

            context.Batches.Add(batch);

            await context.SaveChangesAsync();

            return batch;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new DuplicateException($"A Batch with ID {batch.Id} already exists.");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to create Batch: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    ///     Finds a single batch matching the specified <paramref name="expression"/>.
    /// </summary>
    /// <param name="expression">The expression to use to match batches.</param>
    /// <returns>The found batch, or default if not found.</returns>
    /// <exception cref="ArgumentException">Thrown when an expression is not supplied.</exception>
    public async Task<Batch> FindAsync(Expression<Func<Batch, bool>> expression)
    {
        if (expression == default)
        {
            throw new ArgumentException("An expression must be supplied.", nameof(expression));
        }

        using var context = ContextFactory.CreateDbContext();

        return await context.Batches
            .AsNoTracking()
            .Include(b => b.Transfers)
            .Where(expression)
            .SingleOrDefaultAsync();
    }
}
