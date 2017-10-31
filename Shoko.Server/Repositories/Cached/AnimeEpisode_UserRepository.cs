﻿using System.Collections.Generic;
using System.Linq;
using Shoko.Models.Server;
using NutzCode.InMemoryIndex;
using Shoko.Commons.Extensions;
using Shoko.Models.Client;
using Shoko.Server.Models;
using Shoko.Server.Repositories.NHibernate;


namespace Shoko.Server.Repositories.Cached
{
    public class AnimeEpisode_UserRepository : BaseCachedRepository<SVR_AnimeEpisode_User, int>
    {
        private PocoIndex<int, SVR_AnimeEpisode_User, int> Series;
        private PocoIndex<int, SVR_AnimeEpisode_User, ulong> UsersEpisodes;
        private PocoIndex<int, SVR_AnimeEpisode_User, int> Users;
        private PocoIndex<int, SVR_AnimeEpisode_User, int> Episodes;
        private PocoIndex<int, SVR_AnimeEpisode_User, ulong> UsersSeries;

        private AnimeEpisode_UserRepository()
        {
        }

        public static AnimeEpisode_UserRepository Create()
        {
            return new AnimeEpisode_UserRepository();
        }

        protected override int SelectKey(SVR_AnimeEpisode_User entity)
        {
            return entity.AnimeEpisode_UserID;
        }

        public override void PopulateIndexes()
        {
            Series = Cache.CreateIndex(a => a.AnimeSeriesID);
            UsersEpisodes = Cache.CreateIndex(a => (ulong)a.JMMUserID << 48 | (ulong)a.AnimeEpisodeID);
            Users = Cache.CreateIndex(a => a.JMMUserID);
            Episodes = Cache.CreateIndex(a => a.AnimeEpisodeID);
            UsersSeries = Cache.CreateIndex(a => (ulong)a.JMMUserID << 48 | (ulong)a.AnimeSeriesID);
        }

        public override void RegenerateDb()
        {
            int cnt = 0;
            List<SVR_AnimeEpisode_User> sers =
                Cache.Values.Where(a => a.ContractVersion < SVR_AnimeEpisode_User.CONTRACT_VERSION ||
                                        a.AnimeEpisode_UserID == 0)
                    .ToList();
            int max = sers.Count;
            ServerState.Instance.CurrentSetupStatus = string.Format(Commons.Properties.Resources.Database_Validating,
                typeof(AnimeEpisode_User).Name, " DbRegen");
            if (max <= 0) return;
            foreach (SVR_AnimeEpisode_User g in sers)
            {
                Save(g);
                cnt++;
                if (cnt % 10 == 0)
                    ServerState.Instance.CurrentSetupStatus = string.Format(
                        Commons.Properties.Resources.Database_Validating, typeof(AnimeEpisode_User).Name,
                        " DbRegen - " + cnt + "/" + max);
            }
            ServerState.Instance.CurrentSetupStatus = string.Format(Commons.Properties.Resources.Database_Validating,
                typeof(AnimeEpisode_User).Name,
                " DbRegen - " + max + "/" + max);
        }

        public override void Save(SVR_AnimeEpisode_User obj)
        {
            lock (obj)
            {
                if (obj.AnimeEpisode_UserID == 0)
                    base.Save(obj);
                UpdateContract(obj);
                base.Save(obj);
            }
        }

        public override void SaveWithOpenTransaction(ISessionWrapper session, SVR_AnimeEpisode_User obj)
        {
            lock (obj)
            {
                if (obj.AnimeEpisode_UserID == 0)
                    base.SaveWithOpenTransaction(session, obj);
                UpdateContract(obj);
                base.SaveWithOpenTransaction(session, obj);
            }
        }

        public List<SVR_AnimeEpisode_User> GetBySeriesID(int seriesid)
        {
            lock (Cache)
            {
                return Series.GetMultiple(seriesid);
            }
        }

        public SVR_AnimeEpisode_User GetByUserIDAndEpisodeID(int userid, int epid)
        {
            lock (Cache)
            {
                return UsersEpisodes.GetOne((ulong)userid << 48 | (ulong)epid);
            }
        }


        public List<SVR_AnimeEpisode_User> GetByUserID(int userid)
        {
            lock (Cache)
            {
                return Users.GetMultiple(userid);
            }
        }

        public List<SVR_AnimeEpisode_User> GetMostRecentlyWatched(int userid, int maxresults = 100)
        {
            return GetByUserID(userid).Where(a => a.WatchedCount > 0).OrderByDescending(a => a.WatchedDate)
                .Take(maxresults).ToList();
        }


        public SVR_AnimeEpisode_User GetLastWatchedEpisode()
        {
            lock (Cache)
            {
                return Cache.Values.Where(a => a.WatchedCount > 0).OrderByDescending(a => a.WatchedDate)
                    .FirstOrDefault();
            }
        }

        public SVR_AnimeEpisode_User GetLastWatchedEpisodeForSeries(int seriesid, int userid)
        {
            lock (Cache)
            {
                return UsersSeries.GetMultiple((ulong)userid << 48 | (ulong)seriesid).Where(a => a.WatchedCount > 0)
                    .OrderByDescending(a => a.WatchedDate).FirstOrDefault();
            }
        }

        public List<SVR_AnimeEpisode_User> GetByEpisodeID(int epid)
        {
            lock (Cache)
            {
                return Episodes.GetMultiple(epid);
            }
        }

        public List<SVR_AnimeEpisode_User> GetByUserIDAndSeriesID(int userid, int seriesid)
        {
            lock (Cache)
            {
                return UsersSeries.GetMultiple((ulong)userid << 32 | (ulong)seriesid);
            }
        }


        public void UpdateContract(SVR_AnimeEpisode_User aeu)
        {
            CL_AnimeEpisode_User caep = aeu.Contract ?? new CL_AnimeEpisode_User();
            SVR_AnimeEpisode ep = RepoFactory.AnimeEpisode.GetByID(aeu.AnimeEpisodeID);
            if (ep == null)
                return;
            AniDB_Episode aniEp = ep.AniDB_Episode;
            caep.AniDB_EpisodeID = ep.AniDB_EpisodeID;
            caep.AnimeEpisodeID = ep.AnimeEpisodeID;
            caep.AnimeSeriesID = ep.AnimeSeriesID;
            caep.DateTimeUpdated = ep.DateTimeUpdated;
            caep.PlayedCount = aeu.PlayedCount;
            caep.StoppedCount = aeu.StoppedCount;
            caep.WatchedCount = aeu.WatchedCount;
            caep.WatchedDate = aeu.WatchedDate;
            if (aniEp != null)
            {
                caep.AniDB_AirDate = aniEp.GetAirDateAsDate();
                caep.AniDB_EnglishName = aniEp.EnglishName;
                caep.AniDB_LengthSeconds = aniEp.LengthSeconds;
                caep.AniDB_Rating = aniEp.Rating;
                caep.AniDB_RomajiName = aniEp.RomajiName;
                caep.AniDB_Votes = aniEp.Votes;

                caep.EpisodeNumber = aniEp.EpisodeNumber;
                caep.EpisodeNameRomaji = aniEp.RomajiName;
                caep.EpisodeNameEnglish = aniEp.EnglishName;
                caep.EpisodeType = aniEp.EpisodeType;
            }

            /*
            //TODO if this is needed, calculating it in here will not affect performance
            caep.ReleaseGroups = new List<CL_AniDB_GroupStatus>();
            */
            aeu.Contract = caep;
        }
    }
}