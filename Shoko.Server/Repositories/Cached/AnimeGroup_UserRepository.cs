﻿using System;
using System.Collections.Generic;
using System.Linq;
using NutzCode.InMemoryIndex;
using Shoko.Models.Enums;
using Shoko.Server.Databases;
using Shoko.Server.Models;
using Shoko.Server.Repositories.NHibernate;

namespace Shoko.Server.Repositories.Cached
{
    public class AnimeGroup_UserRepository : BaseCachedRepository<SVR_AnimeGroup_User, int>
    {
        private PocoIndex<int, SVR_AnimeGroup_User, int> Groups;
        private PocoIndex<int, SVR_AnimeGroup_User, int> Users;
        private PocoIndex<int, SVR_AnimeGroup_User, int, int> UsersGroups;
        private Dictionary<int, ChangeTracker<int>> Changes = new();


        public AnimeGroup_UserRepository()
        {
            EndDeleteCallback = cr =>
            {
                if (!Changes.ContainsKey(cr.JMMUserID))
                    Changes[cr.JMMUserID] = new ChangeTracker<int>();
                Changes[cr.JMMUserID].Remove(cr.AnimeGroupID);

                cr.DeleteFromFilters();
            };
        }

        protected override int SelectKey(SVR_AnimeGroup_User entity)
        {
            return entity.AnimeGroup_UserID;
        }

        public override void PopulateIndexes()
        {
            Groups = Cache.CreateIndex(a => a.AnimeGroupID);
            Users = Cache.CreateIndex(a => a.JMMUserID);
            UsersGroups = Cache.CreateIndex(a => a.JMMUserID, a => a.AnimeGroupID);

            foreach (var n in Cache.Values.Select(a => a.JMMUserID).Distinct())
            {
                Changes[n] = new ChangeTracker<int>();
                Changes[n].AddOrUpdateRange(Users.GetMultiple(n).Select(a => a.AnimeGroupID));
            }
        }

        public override void RegenerateDb()
        {
        }

        public override void Save(SVR_AnimeGroup_User obj)
        {
            // Get The previous AnimeGroup_User from db for comparison;
            SVR_AnimeGroup_User old;
            lock (GlobalDBLock)
            {
                using var session = DatabaseFactory.SessionFactory.OpenSession();
                old = session.Get<SVR_AnimeGroup_User>(obj.AnimeGroup_UserID);
            }

            obj.UpdatePlexKodiContracts();
            var types = GetConditionTypesChanged(old, obj);
            base.Save(obj);
            if (!Changes.ContainsKey(obj.JMMUserID))
                Changes[obj.JMMUserID] = new ChangeTracker<int>();
            Changes[obj.JMMUserID].AddOrUpdate(obj.AnimeGroupID);
            obj.UpdateGroupFilters(types);
        }

        private static HashSet<GroupFilterConditionType> GetConditionTypesChanged(SVR_AnimeGroup_User oldcontract, SVR_AnimeGroup_User newcontract)
        {
            var h = new HashSet<GroupFilterConditionType>();

            if (oldcontract == null ||
                oldcontract.UnwatchedEpisodeCount > 0 != newcontract.UnwatchedEpisodeCount > 0)
                h.Add(GroupFilterConditionType.HasUnwatchedEpisodes);
            if (oldcontract == null || oldcontract.IsFave != newcontract.IsFave)
                h.Add(GroupFilterConditionType.Favourite);
            if (oldcontract == null || oldcontract.WatchedDate != newcontract.WatchedDate)
                h.Add(GroupFilterConditionType.EpisodeWatchedDate);
            if (oldcontract == null || oldcontract.WatchedEpisodeCount > 0 != newcontract.WatchedEpisodeCount > 0)
                h.Add(GroupFilterConditionType.HasWatchedEpisodes);
            return h;
        }

        /// <summary>
        /// Inserts a batch of <see cref="SVR_AnimeGroup_User"/> into the database.
        /// </summary>
        /// <remarks>
        /// <para>This method should NOT be used for updating existing entities.</para>
        /// <para>It is up to the caller of this method to manage transactions, etc.</para>
        /// <para>Group Filters, etc. will not be updated by this method.</para>
        /// </remarks>
        /// <param name="session">The NHibernate session.</param>
        /// <param name="groupUsers">The batch of <see cref="SVR_AnimeGroup_User"/> to insert into the database.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="groupUsers"/> is <c>null</c>.</exception>
        public void InsertBatch(ISessionWrapper session, IEnumerable<SVR_AnimeGroup_User> groupUsers)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (groupUsers == null)
                throw new ArgumentNullException(nameof(groupUsers));

            foreach (var groupUser in groupUsers)
            {
                session.Insert(groupUser);

                UpdateCache(groupUser);
                if (!Changes.TryGetValue(groupUser.JMMUserID, out var changeTracker))
                {
                    changeTracker = new ChangeTracker<int>();
                    Changes[groupUser.JMMUserID] = changeTracker;
                }

                changeTracker.AddOrUpdate(groupUser.AnimeGroupID);
            }
        }

        /// <summary>
        /// Inserts a batch of <see cref="SVR_AnimeGroup_User"/> into the database.
        /// </summary>
        /// <remarks>
        /// <para>It is up to the caller of this method to manage transactions, etc.</para>
        /// <para>Group Filters, etc. will not be updated by this method.</para>
        /// </remarks>
        /// <param name="session">The NHibernate session.</param>
        /// <param name="groupUsers">The batch of <see cref="SVR_AnimeGroup_User"/> to insert into the database.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="groupUsers"/> is <c>null</c>.</exception>
        public void UpdateBatch(ISessionWrapper session, IEnumerable<SVR_AnimeGroup_User> groupUsers)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (groupUsers == null)
                throw new ArgumentNullException(nameof(groupUsers));

            foreach (var groupUser in groupUsers)
            {
                session.Update(groupUser);
                UpdateCache(groupUser);

                if (!Changes.TryGetValue(groupUser.JMMUserID, out var changeTracker))
                {
                    changeTracker = new ChangeTracker<int>();
                    Changes[groupUser.JMMUserID] = changeTracker;
                }

                changeTracker.AddOrUpdate(groupUser.AnimeGroupID);
            }
        }

        /// <summary>
        /// Deletes all AnimeGroup_User records.
        /// </summary>
        /// <remarks>
        /// This method also makes sure that the cache is cleared.
        /// </remarks>
        /// <param name="session">The NHibernate session.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        public void DeleteAll(ISessionWrapper session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            // First, get all of the current user/groups so that we can inform the change tracker that they have been removed later
            var usrGrpMap = GetAll().GroupBy(g => g.JMMUserID, g => g.AnimeGroupID);

            lock (GlobalDBLock)
            {
                // Then, actually delete the AnimeGroup_Users
                session.CreateQuery("delete SVR_AnimeGroup_User agu").ExecuteUpdate();
            }

            // Now, update the change trackers with all removed records
            foreach (var grp in usrGrpMap)
            {
                var jmmUserId = grp.Key;

                if (!Changes.TryGetValue(jmmUserId, out var changeTracker))
                {
                    changeTracker = new ChangeTracker<int>();
                    Changes[jmmUserId] = changeTracker;
                }

                changeTracker.RemoveRange(grp);
            }

            // Finally, we need to clear the cache so that it is in sync with the database
            ClearCache();
        }

        public SVR_AnimeGroup_User GetByUserAndGroupID(int userid, int groupid)
        {
            return ReadLock(() => UsersGroups.GetOne(userid, groupid));
        }

        public List<SVR_AnimeGroup_User> GetByUserID(int userid)
        {
            return ReadLock(() => Users.GetMultiple(userid));
        }

        public List<SVR_AnimeGroup_User> GetByGroupID(int groupid)
        {
            return ReadLock(() => Groups.GetMultiple(groupid));
        }

        public ChangeTracker<int> GetChangeTracker(int userid)
        {
            return ReadLock(() => Changes.ContainsKey(userid) ? Changes[userid] : new ChangeTracker<int>());
        }
    }
}
