// <copyright file="SearchExistence.cs" company="JP Dillingham">
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

namespace slskd.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
///     Answers whether a search still exists.
/// </summary>
/// <remarks>
///     <para>
///         A download records the search it was started from on its batch, and batches live in the transfers
///         database while searches live in their own. Nothing joins them and nothing cascades, so deleting a
///         search leaves every download that came from it still naming it -- correctly, since what was searched
///         for is still true, but with a link that now leads nowhere.
///     </para>
///     <para>
///         Cached, because the transfers list is polled once a second while the answer changes only when a search
///         is created, deleted or pruned -- and <see cref="SearchService"/> calls <see cref="Forget"/> at each of
///         those, so the cache is not merely fresh but exact for every change this application makes. The window
///         is what covers anything else, and it is short rather than the half-minute a file's existence gets: both
///         directions of staleness are visible here, as a link that outlives its search and as a link missing from
///         a download enqueued from a search created a moment ago.
///     </para>
///     <para>
///         It takes the context factory rather than <see cref="ISearchService"/> precisely so that the service can
///         depend on this without the two depending on each other.
///     </para>
/// </remarks>
public class SearchExistence
{
    private const int LifetimeSeconds = 5;
    private const string CacheKey = "search-ids";

    /// <summary>
    ///     Initializes a new instance of the <see cref="SearchExistence"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory to use.</param>
    public SearchExistence(IDbContextFactory<SearchDbContext> contextFactory)
    {
        ContextFactory = contextFactory;
    }

    private IDbContextFactory<SearchDbContext> ContextFactory { get; }
    private IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    ///     Gets a value indicating whether the search with the specified <paramref name="id"/> still exists.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <returns>Whether it exists.</returns>
    public async Task<bool> ExistsAsync(Guid id)
    {
        return (await IdsAsync()).Contains(id);
    }

    /// <summary>
    ///     Forgets the cached answer, so that the next question is asked of the database.
    /// </summary>
    public void Forget() => Cache.Remove(CacheKey);

    /// <summary>
    ///     The ids of every search there is.
    /// </summary>
    /// <remarks>
    ///     The whole set rather than a lookup per id: it is a projection to the primary key, so the database
    ///     answers it from the index without reading a row, and a list of downloads naming twenty searches would
    ///     otherwise be twenty queries where this is one.
    /// </remarks>
    /// <returns>The ids.</returns>
    private async Task<HashSet<Guid>> IdsAsync()
    {
        if (Cache.TryGetValue<HashSet<Guid>>(CacheKey, out var cached))
        {
            return cached;
        }

        using var context = await ContextFactory.CreateDbContextAsync();

        var ids = (await context.Searches
            .AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync())
            .ToHashSet();

        Cache.Set(
            key: CacheKey,
            value: ids,
            absoluteExpirationRelativeToNow: TimeSpan.FromSeconds(LifetimeSeconds));

        return ids;
    }
}
