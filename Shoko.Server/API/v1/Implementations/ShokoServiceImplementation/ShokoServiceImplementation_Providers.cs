﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Shoko.Commons.Extensions;
using Shoko.Models.Azure;
using Shoko.Models.Client;
using Shoko.Models.Enums;
using Shoko.Models.Interfaces;
using Shoko.Models.Server;
using Shoko.Models.TvDB;
using Shoko.Server.Commands;
using Shoko.Server.Commands.TvDB;
using Shoko.Server.Databases;
using Shoko.Server.Extensions;
using Shoko.Server.Models;
using Shoko.Server.Providers.MovieDB;
using Shoko.Server.Providers.TraktTV;
using Shoko.Server.Providers.TraktTV.Contracts;
using Shoko.Server.Providers.TvDB;
using Shoko.Server.Repositories;
using Shoko.Server.Settings;

namespace Shoko.Server
{
    public partial class ShokoServiceImplementation : IShokoServer
    {
        [HttpGet("AniDB/CrossRef/{animeID}")]
        public CL_AniDB_AnimeCrossRefs GetCrossRefDetails(int animeID)
        {
            CL_AniDB_AnimeCrossRefs result = new CL_AniDB_AnimeCrossRefs
            {
                CrossRef_AniDB_TvDB = new List<CrossRef_AniDB_TvDBV2>(),
                TvDBSeries = new List<TvDB_Series>(),
                TvDBEpisodes = new List<TvDB_Episode>(),
                TvDBImageFanarts = new List<TvDB_ImageFanart>(),
                TvDBImagePosters = new List<TvDB_ImagePoster>(),
                TvDBImageWideBanners = new List<TvDB_ImageWideBanner>(),

                CrossRef_AniDB_MovieDB = null,
                MovieDBMovie = null,
                MovieDBFanarts = new List<MovieDB_Fanart>(),
                MovieDBPosters = new List<MovieDB_Poster>(),

                CrossRef_AniDB_MAL = null,

                CrossRef_AniDB_Trakt = new List<CrossRef_AniDB_TraktV2>(),
                TraktShows = new List<CL_Trakt_Show>(),
                AnimeID = animeID
            };

            try
            {
                using (var session = DatabaseFactory.SessionFactory.OpenSession())
                {
                    SVR_AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(animeID);
                    if (anime == null) return result;

                    var xrefs = RepoFactory.CrossRef_AniDB_TvDB.GetV2LinksFromAnime(animeID);

                    // TvDB
                    result.CrossRef_AniDB_TvDB = xrefs;

                    foreach (TvDB_Episode ep in anime.GetTvDBEpisodes())
                        result.TvDBEpisodes.Add(ep);

                    foreach (var xref in xrefs.DistinctBy(a => a.TvDBID))
                    {
                        TvDB_Series ser = RepoFactory.TvDB_Series.GetByTvDBID(xref.TvDBID);
                        if (ser != null)
                            result.TvDBSeries.Add(ser);

                        foreach (TvDB_ImageFanart fanart in RepoFactory.TvDB_ImageFanart.GetBySeriesID(xref.TvDBID))
                            result.TvDBImageFanarts.Add(fanart);

                        foreach (TvDB_ImagePoster poster in RepoFactory.TvDB_ImagePoster.GetBySeriesID(xref.TvDBID))
                            result.TvDBImagePosters.Add(poster);

                        foreach (TvDB_ImageWideBanner banner in RepoFactory.TvDB_ImageWideBanner.GetBySeriesID(xref
                            .TvDBID))
                            result.TvDBImageWideBanners.Add(banner);
                    }

                    // Trakt


                    foreach (CrossRef_AniDB_TraktV2 xref in anime.GetCrossRefTraktV2(session))
                    {
                        result.CrossRef_AniDB_Trakt.Add(xref);

                        Trakt_Show show = RepoFactory.Trakt_Show.GetByTraktSlug(session, xref.TraktID);
                        if (show != null) result.TraktShows.Add(show.ToClient());
                    }


                    // MovieDB
                    CrossRef_AniDB_Other xrefMovie = anime.GetCrossRefMovieDB();
                    result.CrossRef_AniDB_MovieDB = xrefMovie;


                    result.MovieDBMovie = anime.GetMovieDBMovie();


                    foreach (MovieDB_Fanart fanart in anime.GetMovieDBFanarts())
                    {
                        if (fanart.ImageSize.Equals(Shoko.Models.Constants.MovieDBImageSize.Original,
                            StringComparison.InvariantCultureIgnoreCase))
                            result.MovieDBFanarts.Add(fanart);
                    }

                    foreach (MovieDB_Poster poster in anime.GetMovieDBPosters())
                    {
                        if (poster.ImageSize.Equals(Shoko.Models.Constants.MovieDBImageSize.Original,
                            StringComparison.InvariantCultureIgnoreCase))
                            result.MovieDBPosters.Add(poster);
                    }

                    // MAL
                    List<CrossRef_AniDB_MAL> xrefMAL = anime.GetCrossRefMAL();
                    if (xrefMAL == null)
                        result.CrossRef_AniDB_MAL = null;
                    else
                    {
                        result.CrossRef_AniDB_MAL = new List<CrossRef_AniDB_MAL>();
                        foreach (CrossRef_AniDB_MAL xrefTemp in xrefMAL)
                            result.CrossRef_AniDB_MAL.Add(xrefTemp);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return result;
            }
        }

        #region Web Cache Admin

        [HttpGet("WebCache/IsAdmin")]
        public bool IsWebCacheAdmin()
        {
            return false;
        }

        [HttpGet("WebCache/RandomLinkForApproval/{linkType}")]
        public Azure_AnimeLink Admin_GetRandomLinkForApproval(int linkType)
        {
            return null;
        }

        [HttpGet("WebCache/AdminMessages")]
        public List<Azure_AdminMessage> GetAdminMessages()
        {
            try
            {
                return ServerInfo.Instance.AdminMessages?.ToList() ?? new List<Azure_AdminMessage>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<Azure_AdminMessage>();
            }
        }

        #region Admin - TvDB

        [HttpGet("WebCache/CrossRef/TvDB/{crossRef_AniDB_TvDBId}")]
        public string ApproveTVDBCrossRefWebCache(int crossRef_AniDB_TvDBId)
        {
            return "This feature has been disabled until further notice.";
        }

        [HttpDelete("WebCache/CrossRef/TvDB/{crossRef_AniDB_TvDBId}")]
        public string RevokeTVDBCrossRefWebCache(int crossRef_AniDB_TvDBId)
        {
            return "This feature has been disabled until further notice.";
        }

        /// <summary>
        /// Sends the current user's TvDB links to the web cache, and then admin approves them
        /// </summary>
        /// <returns></returns>
        [HttpPost("WebCache/TvDB/UseLinks/{animeID}")]
        public string UseMyTvDBLinksWebCache(int animeID)
        {
            return "This feature has been disabled until further notice.";
        }

        #endregion

        #region Admin - Trakt

        [HttpPost("WebCache/CrossRef/Trakt/{crossRef_AniDB_TraktId}")]
        public string ApproveTraktCrossRefWebCache(int crossRef_AniDB_TraktId)
        {
            return "This feature has been disabled until further notice.";
        }

        [HttpPost("WebCache/CrossRef/Trakt/{crossRef_AniDB_TraktId}")]
        public string RevokeTraktCrossRefWebCache(int crossRef_AniDB_TraktId)
        {
            return "This feature has been disabled until further notice.";
        }

        /// <summary>
        /// Sends the current user's Trakt links to the web cache, and then admin approves them
        /// </summary>
        /// <returns></returns>
        [HttpPost("WebCache/Trakt/UseLinks/{animeID}")]
        public string UseMyTraktLinksWebCache(int animeID)
        {
            return "This feature has been disabled until further notice.";
        }

        #endregion

        #endregion

        #region TvDB

        [HttpPost("Series/TvDB/Refresh/{seriesID}")]
        public string UpdateTvDBData(int seriesID)
        {
            try
            {
                CommandRequest_TvDBUpdateSeries updateseries = _commandFactory.Create<CommandRequest_TvDBUpdateSeries>(
                    c =>
                    {
                        c.TvDBSeriesID = seriesID;
                        c.ForceRefresh = true;
                    }
                );
                updateseries.Save();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }
            return string.Empty;
        }

        [HttpGet("TvDB/Language")]
        public List<TvDB_Language> GetTvDBLanguages()
        {
            try
            {
                return _tvdbHelper.GetLanguages();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }

            return new List<TvDB_Language>();
        }

        [HttpGet("WebCache/CrossRef/TvDB/{animeID}/{isAdmin}")]
        public List<Azure_CrossRef_AniDB_TvDB> GetTVDBCrossRefWebCache(int animeID, bool isAdmin)
        {
            try
            {
                return new List<Azure_CrossRef_AniDB_TvDB>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<Azure_CrossRef_AniDB_TvDB>();
            }
        }
        
        [HttpGet("TvDB/CrossRef/{animeID}")]
        public List<CrossRef_AniDB_TvDBV2> GetTVDBCrossRefV2(int animeID)
        {
            try
            {
                return RepoFactory.CrossRef_AniDB_TvDB.GetV2LinksFromAnime(animeID);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new();
            }
        }

        [HttpGet("TvDB/CrossRef/Preview/{animeID}/{tvdbID}")]
        public List<CrossRef_AniDB_TvDB_Episode> GetTvDBEpisodeMatchPreview(int animeID, int tvdbID)
        {
            return TvDBLinkingHelper.GetMatchPreviewWithOverrides(animeID, tvdbID);
        }

        [HttpGet("TvDB/CrossRef/Episode/{animeID}")]
        public List<CrossRef_AniDB_TvDB_Episode_Override> GetTVDBCrossRefEpisode(int animeID)
        {
            try
            {
                return RepoFactory.CrossRef_AniDB_TvDB_Episode_Override.GetByAnimeID(animeID).ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new ();
            }
        }

        [HttpGet("TvDB/Search/{criteria}")]
        public List<TVDB_Series_Search_Response> SearchTheTvDB(string criteria)
        {
            try
            {
                return _tvdbHelper.SearchSeries(criteria);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new ();
            }
        }

        [HttpGet("Series/Seasons/{seriesID}")]
        public List<int> GetSeasonNumbersForSeries(int seriesID)
        {
            List<int> seasonNumbers = new List<int>();
            try
            {
                // refresh data from TvDB
                _tvdbHelper.UpdateSeriesInfoAndImages(seriesID, true, false);

                seasonNumbers = RepoFactory.TvDB_Episode.GetSeasonNumbersForSeries(seriesID);

                return seasonNumbers;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return seasonNumbers;
            }
        }

        [HttpPost("TvDB/CrossRef")]
        public string LinkAniDBTvDB(CrossRef_AniDB_TvDBV2 link)
        {
            try
            {
                CrossRef_AniDB_TvDB xref = RepoFactory.CrossRef_AniDB_TvDB.GetByAniDBAndTvDBID(link.AnimeID, link.TvDBID);

                if (xref != null && link.IsAdditive)
                {
                    string msg = $"You have already linked Anime ID {xref.AniDBID} to this TvDB show/season/ep";
                    SVR_AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(xref.AniDBID);
                    if (anime != null)
                        msg =
                            $"You have already linked Anime {anime.MainTitle} ({xref.AniDBID}) to this TvDB show/season/ep";
                    return msg;
                }

                // we don't need to proactively remove the link here anymore, as all links are removed when it is not marked as additive
                CommandRequest_LinkAniDBTvDB cmdRequest = _commandFactory.Create<CommandRequest_LinkAniDBTvDB>(
                    c =>
                    {
                        c.AnimeID = link.AnimeID;
                        c.TvDBID = link.TvDBID;
                        c.AdditiveLink = link.IsAdditive;
                    }
                );
                cmdRequest.Save();

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpPost("TvDB/CrossRef/FromWebCache")]
        public string LinkTvDBUsingWebCacheLinks(List<CrossRef_AniDB_TvDBV2> links)
        {
            try
            {
                if (!ServerSettings.Instance.WebCache.Enabled) return "The WebCache is disabled.";
                if (links.Count == 0) return "No links were given in the request. This is a bug.";

                var link = links[0];

                var existingLinks = RepoFactory.CrossRef_AniDB_TvDB.GetByAnimeID(link.AnimeID);
                RepoFactory.CrossRef_AniDB_TvDB.Delete(existingLinks);
                RepoFactory.CrossRef_AniDB_TvDB_Episode.DeleteAllUnverifiedLinksForAnime(link.AnimeID);

                // we don't need to proactively remove the link here anymore, as all links are removed when it is not marked as additive
                CommandRequest_LinkAniDBTvDB cmdRequest = _commandFactory.Create<CommandRequest_LinkAniDBTvDB>(
                    c =>
                    {
                        c.AnimeID = link.AnimeID;
                        c.TvDBID = link.TvDBID;
                        c.AdditiveLink = link.IsAdditive;
                    }
                );
                cmdRequest.Save();

                var overrides = TvDBLinkingHelper.GetSpecialsOverridesFromLegacy(links);
                foreach (var episodeOverride in overrides)
                {
                    var exists =
                        RepoFactory.CrossRef_AniDB_TvDB_Episode_Override.GetByAniDBAndTvDBEpisodeIDs(
                            episodeOverride.AniDBEpisodeID, episodeOverride.TvDBEpisodeID);
                    if (exists != null) continue;
                    RepoFactory.CrossRef_AniDB_TvDB_Episode_Override.Save(episodeOverride);
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpPost("TvDB/CrossRef/Episode/{aniDBID}/{tvDBID}")]
        public string LinkAniDBTvDBEpisode(int aniDBID, int tvDBID)
        {
            try
            {
                _tvdbHelper.LinkAniDBTvDBEpisode(aniDBID, tvDBID);

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        /// <summary>
        /// Removes all tvdb links for one anime
        /// </summary>
        /// <param name="animeID"></param>
        /// <returns></returns>
        [HttpDelete("TvDB/CrossRef/{animeID}")]
        public string RemoveLinkAniDBTvDBForAnime(int animeID)
        {
            try
            {
                SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(animeID);

                if (ser == null) return "Could not find Series for Anime!";

                List<CrossRef_AniDB_TvDB> xrefs = RepoFactory.CrossRef_AniDB_TvDB.GetByAnimeID(animeID);
                if (xrefs == null) return string.Empty;

                foreach (CrossRef_AniDB_TvDB xref in xrefs)
                {
                    // check if there are default images used associated
                    List<AniDB_Anime_DefaultImage> images = RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeID(animeID);
                    foreach (AniDB_Anime_DefaultImage image in images)
                    {
                        if (image.ImageParentType == (int) ImageEntityType.TvDB_Banner ||
                            image.ImageParentType == (int) ImageEntityType.TvDB_Cover ||
                            image.ImageParentType == (int) ImageEntityType.TvDB_FanArt)
                        {
                            if (image.ImageParentID == xref.TvDBID)
                                RepoFactory.AniDB_Anime_DefaultImage.Delete(image.AniDB_Anime_DefaultImageID);
                        }
                    }

                    _tvdbHelper.RemoveLinkAniDBTvDB(xref.AniDBID, xref.TvDBID);
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpDelete("TvDB/CrossRef")]
        public string RemoveLinkAniDBTvDB(CrossRef_AniDB_TvDBV2 link)
        {
            try
            {
                SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(link.AnimeID);

                if (ser == null) return "Could not find Series for Anime!";

                // check if there are default images used associated
                List<AniDB_Anime_DefaultImage> images = RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeID(link.AnimeID);
                foreach (AniDB_Anime_DefaultImage image in images)
                {
                    if (image.ImageParentType == (int) ImageEntityType.TvDB_Banner ||
                        image.ImageParentType == (int) ImageEntityType.TvDB_Cover ||
                        image.ImageParentType == (int) ImageEntityType.TvDB_FanArt)
                    {
                        if (image.ImageParentID == link.TvDBID)
                            RepoFactory.AniDB_Anime_DefaultImage.Delete(image.AniDB_Anime_DefaultImageID);
                    }
                }

                _tvdbHelper.RemoveLinkAniDBTvDB(link.AnimeID, link.TvDBID);

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpDelete("TvDB/CrossRef/Episode/{aniDBEpisodeID}/{tvDBEpisodeID}")]
        public string RemoveLinkAniDBTvDBEpisode(int aniDBEpisodeID, int tvDBEpisodeID)
        {
            try
            {
                AniDB_Episode ep = RepoFactory.AniDB_Episode.GetByEpisodeID(aniDBEpisodeID);

                if (ep == null) return "Could not find Episode";

                CrossRef_AniDB_TvDB_Episode_Override xref =
                    RepoFactory.CrossRef_AniDB_TvDB_Episode_Override.GetByAniDBAndTvDBEpisodeIDs(aniDBEpisodeID,
                        tvDBEpisodeID);
                if (xref == null) return "Could not find Link!";


                RepoFactory.CrossRef_AniDB_TvDB_Episode_Override.Delete(xref.CrossRef_AniDB_TvDB_Episode_OverrideID);

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpGet("TvDB/Poster/{tvDBID?}")]
        public List<TvDB_ImagePoster> GetAllTvDBPosters(int? tvDBID)
        {
            List<TvDB_ImagePoster> allImages = new List<TvDB_ImagePoster>();
            try
            {
                if (tvDBID.HasValue)
                    return RepoFactory.TvDB_ImagePoster.GetBySeriesID(tvDBID.Value);
                return RepoFactory.TvDB_ImagePoster.GetAll().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<TvDB_ImagePoster>();
            }
        }

        [HttpGet("TvDB/Banner/{tvDBID?}")]
        public List<TvDB_ImageWideBanner> GetAllTvDBWideBanners(int? tvDBID)
        {
            try
            {
                if (tvDBID.HasValue)
                    return RepoFactory.TvDB_ImageWideBanner.GetBySeriesID(tvDBID.Value);
                return RepoFactory.TvDB_ImageWideBanner.GetAll().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<TvDB_ImageWideBanner>();
            }
        }

        [HttpGet("TvDB/Fanart/{tvDBID?}")]
        public List<TvDB_ImageFanart> GetAllTvDBFanart(int? tvDBID)
        {
            try
            {
                if (tvDBID.HasValue)
                    return RepoFactory.TvDB_ImageFanart.GetBySeriesID(tvDBID.Value);
                return RepoFactory.TvDB_ImageFanart.GetAll().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<TvDB_ImageFanart>();
            }
        }

        [HttpGet("TvDB/Episode/{tvDBID?}")]
        public List<TvDB_Episode> GetAllTvDBEpisodes(int? tvDBID)
        {
            try
            {
                if (tvDBID.HasValue)
                    return RepoFactory.TvDB_Episode.GetBySeriesID(tvDBID.Value);
                return RepoFactory.TvDB_Episode.GetAll().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<TvDB_Episode>();
            }
        }

        #endregion

        #region Trakt

        [HttpGet("Trakt/Episode/{traktShowID?}")]
        public List<Trakt_Episode> GetAllTraktEpisodes(int? traktShowID)
        {
            try
            {
                if (traktShowID.HasValue)
                    return RepoFactory.Trakt_Episode.GetByShowID(traktShowID.Value).ToList();
                return RepoFactory.Trakt_Episode.GetAll().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<Trakt_Episode>();
            }
        }

        [HttpGet("Trakt/Episode/FromTraktId/{traktID}")]
        public List<Trakt_Episode> GetAllTraktEpisodesByTraktID(string traktID)
        {
            try
            {
                Trakt_Show show = RepoFactory.Trakt_Show.GetByTraktSlug(traktID);
                if (show != null)
                    return GetAllTraktEpisodes(show.Trakt_ShowID);

                return new List<Trakt_Episode>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<Trakt_Episode>();
            }
        }

        [HttpGet("WebCache/CrossRef/Trakt/{animeID}/{isAdmin}")]
        public List<Azure_CrossRef_AniDB_Trakt> GetTraktCrossRefWebCache(int animeID, bool isAdmin)
        {
            try
            {
                return new List<Azure_CrossRef_AniDB_Trakt>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<Azure_CrossRef_AniDB_Trakt>();
            }
        }

        [HttpPost("Trakt/CrossRef/{animeID}/{aniEpType}/{aniEpNumber}/{traktID}/{seasonNumber}/{traktEpNumber}/{crossRef_AniDB_TraktV2ID?}")]
        public string LinkAniDBTrakt(int animeID, int aniEpType, int aniEpNumber, string traktID, int seasonNumber,
            int traktEpNumber, int? crossRef_AniDB_TraktV2ID)
        {
            try
            {
                if (crossRef_AniDB_TraktV2ID.HasValue)
                {
                    CrossRef_AniDB_TraktV2 xrefTemp =
                        RepoFactory.CrossRef_AniDB_TraktV2.GetByID(crossRef_AniDB_TraktV2ID.Value);
                    // delete the existing one if we are updating
                    _traktHelper.RemoveLinkAniDBTrakt(xrefTemp.AnimeID, (EpisodeType) xrefTemp.AniDBStartEpisodeType,
                        xrefTemp.AniDBStartEpisodeNumber,
                        xrefTemp.TraktID, xrefTemp.TraktSeasonNumber, xrefTemp.TraktStartEpisodeNumber);
                }

                CrossRef_AniDB_TraktV2 xref = RepoFactory.CrossRef_AniDB_TraktV2.GetByTraktID(traktID, seasonNumber,
                    traktEpNumber, animeID,
                    aniEpType,
                    aniEpNumber);
                if (xref != null)
                {
                    string msg = string.Format("You have already linked Anime ID {0} to this Trakt show/season/ep",
                        xref.AnimeID);
                    SVR_AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(xref.AnimeID);
                    if (anime != null)
                    {
                        msg = string.Format("You have already linked Anime {0} ({1}) to this Trakt show/season/ep",
                            anime.MainTitle,
                            xref.AnimeID);
                    }
                    return msg;
                }

                return _traktHelper.LinkAniDBTrakt(animeID, (EpisodeType) aniEpType, aniEpNumber, traktID,
                    seasonNumber,
                    traktEpNumber, false);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpGet("Trakt/CrossRef/{animeID}")]
        public List<CrossRef_AniDB_TraktV2> GetTraktCrossRefV2(int animeID)
        {
            try
            {
                return RepoFactory.CrossRef_AniDB_TraktV2.GetByAnimeID(animeID).ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new();
            }
        }

        [HttpGet("Trakt/CrossRef/Episode/{animeID}")]
        public List<CrossRef_AniDB_Trakt_Episode> GetTraktCrossRefEpisode(int animeID)
        {
            try
            {
                return RepoFactory.CrossRef_AniDB_Trakt_Episode.GetByAnimeID(animeID);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new();
            }
        }

        [HttpGet("Trakt/Search/{criteria}")]
        public List<CL_TraktTVShowResponse> SearchTrakt(string criteria)
        {
            List<CL_TraktTVShowResponse> results = new List<CL_TraktTVShowResponse>();
            try
            {
                List<TraktV2SearchShowResult> traktResults = _traktHelper.SearchShowV2(criteria);

                foreach (TraktV2SearchShowResult res in traktResults)
                    results.Add(res.ToContract());

                return results;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return results;
            }
        }

        [HttpDelete("Trakt/CrossRef/{animeID}")]
        public string RemoveLinkAniDBTraktForAnime(int animeID)
        {
            try
            {
                SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(animeID);

                if (ser == null) return "Could not find Series for Anime!";

                // check if there are default images used associated
                List<AniDB_Anime_DefaultImage> images = RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeID(animeID);
                foreach (AniDB_Anime_DefaultImage image in images)
                {
                    if (image.ImageParentType == (int) ImageEntityType.Trakt_Fanart ||
                        image.ImageParentType == (int) ImageEntityType.Trakt_Poster)
                    {
                        RepoFactory.AniDB_Anime_DefaultImage.Delete(image.AniDB_Anime_DefaultImageID);
                    }
                }

                foreach (CrossRef_AniDB_TraktV2 xref in RepoFactory.CrossRef_AniDB_TraktV2.GetByAnimeID(animeID))
                {
                    _traktHelper.RemoveLinkAniDBTrakt(animeID, (EpisodeType) xref.AniDBStartEpisodeType,
                        xref.AniDBStartEpisodeNumber,
                        xref.TraktID, xref.TraktSeasonNumber, xref.TraktStartEpisodeNumber);
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpDelete("Trakt/CrossRef/{animeID}/{aniEpType}/{aniEpNumber}/{traktID}/{traktSeasonNumber}/{traktEpNumber}")]
        public string RemoveLinkAniDBTrakt(int animeID, int aniEpType, int aniEpNumber, string traktID,
            int traktSeasonNumber,
            int traktEpNumber)
        {
            try
            {
                SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(animeID);

                if (ser == null) return "Could not find Series for Anime!";

                // check if there are default images used associated
                List<AniDB_Anime_DefaultImage> images = RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeID(animeID);
                foreach (AniDB_Anime_DefaultImage image in images)
                {
                    if (image.ImageParentType == (int) ImageEntityType.Trakt_Fanart ||
                        image.ImageParentType == (int) ImageEntityType.Trakt_Poster)
                    {
                        RepoFactory.AniDB_Anime_DefaultImage.Delete(image.AniDB_Anime_DefaultImageID);
                    }
                }

                _traktHelper.RemoveLinkAniDBTrakt(animeID, (EpisodeType) aniEpType, aniEpNumber,
                    traktID, traktSeasonNumber, traktEpNumber);

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpGet("Trakt/Seasons/{traktID}")]
        public List<int> GetSeasonNumbersForTrakt(string traktID)
        {
            List<int> seasonNumbers = new List<int>();
            try
            {
                // refresh show info including season numbers from trakt
                TraktV2ShowExtended tvshow = _traktHelper.GetShowInfoV2(traktID);

                Trakt_Show show = RepoFactory.Trakt_Show.GetByTraktSlug(traktID);
                if (show == null) return seasonNumbers;

                foreach (Trakt_Season season in show.GetSeasons())
                    seasonNumbers.Add(season.Season);

                return seasonNumbers;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return seasonNumbers;
            }
        }

        [HttpDelete("Trakt/Friend/{friendUsername}")]
        public CL_Response<bool> TraktFriendRequestDeny(string friendUsername)
        {
            return new CL_Response<bool> {Result = false};
            /*
            try
            {
                return TraktTVHelper.FriendRequestDeny(friendUsername, ref returnMessage);
            }
            catch (Exception ex)
            {
                logger.Error( ex,"Error in TraktFriendRequestDeny: " + ex.ToString());
                returnMessage = ex.Message;
                return false;
            }*/
        }

        [HttpPost("Trakt/Friend/{friendUsername}")]
        public CL_Response<bool> TraktFriendRequestApprove(string friendUsername)
        {
            return new CL_Response<bool> {Result = false};
            /*
            try
            {
                return TraktTVHelper.FriendRequestApprove(friendUsername, ref returnMessage);
            }
            catch (Exception ex)
            {
                logger.Error( ex,"Error in TraktFriendRequestDeny: " + ex.ToString());
                returnMessage = ex.Message;
                return false;
            }*/
        }

        [HttpPost("Trakt/Scrobble/{animeId}/{type}/{progress}/{status}")]
        public int TraktScrobble(int animeId, int type, int progress, int status)
        {
            try
            {
                ScrobblePlayingStatus statusTraktV2 = ScrobblePlayingStatus.Start;

                switch (status)
                {
                    case (int)ScrobblePlayingStatus.Start:
                        statusTraktV2 = ScrobblePlayingStatus.Start;
                        break;
                    case (int)ScrobblePlayingStatus.Pause:
                        statusTraktV2 = ScrobblePlayingStatus.Pause;
                        break;
                    case (int)ScrobblePlayingStatus.Stop:
                        statusTraktV2 = ScrobblePlayingStatus.Stop;
                        break;
                }

                bool isValidProgress = float.TryParse(progress.ToString(), out float progressTrakt);

                if (isValidProgress)
                {
                    switch (type)
                    {
                        // Movie
                        case (int) ScrobblePlayingType.movie:
                            return _traktHelper.Scrobble(
                                ScrobblePlayingType.movie, animeId.ToString(),
                                statusTraktV2, progressTrakt);
                        // TV episode
                        case (int) ScrobblePlayingType.episode:
                            return _traktHelper.Scrobble(
                                ScrobblePlayingType.episode,
                                animeId.ToString(), statusTraktV2, progressTrakt);
                    }
                }
                return 500;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return 500;
            }
        }

        [HttpPost("Trakt/Refresh/{traktID}")]
        public string UpdateTraktData(string traktID)
        {
            try
            {
                _traktHelper.UpdateAllInfo(traktID);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }
            return string.Empty;
        }

        [HttpPost("Trakt/Sync/{animeID}")]
        public string SyncTraktSeries(int animeID)
        {
            try
            {
                if (!ServerSettings.Instance.TraktTv.Enabled) return string.Empty;

                SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(animeID);
                if (ser == null) return "Could not find Anime Series";

                CommandRequest_TraktSyncCollectionSeries cmd = _commandFactory.Create<CommandRequest_TraktSyncCollectionSeries>(
                    c =>
                    {
                        c.AnimeSeriesID = ser.AnimeSeriesID;
                        c.SeriesName = ser.GetSeriesName();
                    }
                );
                cmd.Save();

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpPost("Trakt/Comment/{traktID}/{isSpoiler}")]
        public CL_Response<bool> PostTraktCommentShow(string traktID, string commentText, bool isSpoiler)
        {
            return _traktHelper.PostCommentShow(traktID, commentText, isSpoiler);
        }

        [HttpPost("Trakt/LinkValidity/{slug}/{removeDBEntries}")]
        public bool CheckTraktLinkValidity(string slug, bool removeDBEntries)
        {
            try
            {
                return _traktHelper.CheckTraktValidity(slug, removeDBEntries);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }
            return false;
        }

        [HttpGet("Trakt/CrossRef")]
        public List<CrossRef_AniDB_TraktV2> GetAllTraktCrossRefs()
        {
            try
            {
                return RepoFactory.CrossRef_AniDB_TraktV2.GetAll().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }
            return new List<CrossRef_AniDB_TraktV2>();
        }

        [HttpGet("Trakt/Comment/{animeID}")]
        public List<CL_Trakt_CommentUser> GetTraktCommentsForAnime(int animeID)
        {
            return new List<CL_Trakt_CommentUser>();
        }

        [HttpGet("Trakt/DeviceCode")]
        public CL_TraktDeviceCode GetTraktDeviceCode()
        {
            try
            {
                var response = _traktHelper.GetTraktDeviceCode();
                return new CL_TraktDeviceCode
                {
                    VerificationUrl = response.VerificationUrl,
                    UserCode = response.UserCode
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in GetTraktDeviceCode: " + ex);
                return null;
            }
        }

        #endregion

        #region Other Cross Refs

        [HttpGet("WebCache/CrossRef/Other/{animeID}/{crossRefType}")]
        public CL_CrossRef_AniDB_Other_Response GetOtherAnimeCrossRefWebCache(int animeID, int crossRefType)
        {
            try
            {
                return new CL_CrossRef_AniDB_Other_Response();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return null;
            }
        }

        [HttpGet("Other/CrossRef/{animeID}/{crossRefType}")]
        public CrossRef_AniDB_Other GetOtherAnimeCrossRef(int animeID, int crossRefType)
        {
            try
            {
                return RepoFactory.CrossRef_AniDB_Other.GetByAnimeIDAndType(animeID, (CrossRefType) crossRefType);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return null;
            }
        }

        [HttpPost("Other/CrossRef/{animeID}/{id}/{crossRefType}")]
        public string LinkAniDBOther(int animeID, int id, int crossRefType)
        {
            try
            {
                CrossRefType xrefType = (CrossRefType) crossRefType;

                switch (xrefType)
                {
                    case CrossRefType.MovieDB:
                        _movieDBHelper.LinkAniDBMovieDB(animeID, id, false);
                        break;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        [HttpDelete("Other/CrossRef/{animeID}/{crossRefType}")]
        public string RemoveLinkAniDBOther(int animeID, int crossRefType)
        {
            try
            {
                SVR_AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(animeID);

                if (anime == null) return "Could not find Anime!";

                CrossRefType xrefType = (CrossRefType) crossRefType;
                switch (xrefType)
                {
                    case CrossRefType.MovieDB:

                        // check if there are default images used associated
                        List<AniDB_Anime_DefaultImage> images =
                            RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeID(animeID);
                        foreach (AniDB_Anime_DefaultImage image in images)
                        {
                            if (image.ImageParentType == (int) ImageEntityType.MovieDB_FanArt ||
                                image.ImageParentType == (int) ImageEntityType.MovieDB_Poster)
                            {
                                RepoFactory.AniDB_Anime_DefaultImage.Delete(image.AniDB_Anime_DefaultImageID);
                            }
                        }
                        _movieDBHelper.RemoveLinkAniDBMovieDB(animeID);
                        break;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return ex.Message;
            }
        }

        #endregion

        #region MovieDB

        [HttpGet("MovieDB/Search/{criteria}")]
        public List<CL_MovieDBMovieSearch_Response> SearchTheMovieDB(string criteria)
        {
            List<CL_MovieDBMovieSearch_Response> results = new List<CL_MovieDBMovieSearch_Response>();
            try
            {
                List<MovieDB_Movie_Result> movieResults = _movieDBHelper.Search(criteria);

                foreach (MovieDB_Movie_Result res in movieResults)
                    results.Add(res.ToContract());

                return results;
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return results;
            }
        }

        [HttpGet("MovieDB/Poster/{movieID?}")]
        public List<MovieDB_Poster> GetAllMovieDBPosters(int? movieID)
        {
            try
            {
                if (movieID.HasValue)
                    return RepoFactory.MovieDB_Poster.GetByMovieID(movieID.Value);
                return RepoFactory.MovieDB_Poster.GetAllOriginal();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<MovieDB_Poster>();
            }
        }

        [HttpGet("MovieDB/Fanart/{movieID?}")]
        public List<MovieDB_Fanart> GetAllMovieDBFanart(int? movieID)
        {
            try
            {
                if (movieID.HasValue)
                    return RepoFactory.MovieDB_Fanart.GetByMovieID(movieID.Value);
                return RepoFactory.MovieDB_Fanart.GetAllOriginal();
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
                return new List<MovieDB_Fanart>();
            }
        }

        [HttpPost("MovieDB/Refresh/{movieID}")]
        public string UpdateMovieDBData(int movieD)
        {
            try
            {
                _movieDBHelper.UpdateMovieInfo(movieD, true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.ToString());
            }
            return string.Empty;
        }

        #endregion
    }
}
