﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using GitHub.Api;
using GitHub.Caches;
using GitHub.Extensions;
using GitHub.Models;
using GitHub.Primitives;
using Octokit.GraphQL;
using static Octokit.GraphQL.Variable;

namespace GitHub.Services
{
    /// <summary>
    /// Supplies @mentions auto complete suggestions.
    /// </summary>
    [Export(typeof(IAutoCompleteSource))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MentionsAutoCompleteSource : IAutoCompleteSource
    {
        readonly ITeamExplorerContext teamExplorerContext;
        readonly IGraphQLClientFactory graphqlFactory;
        readonly IAvatarProvider avatarProvider;
        ICompiledQuery<List<SuggestionItem>> query;

        [ImportingConstructor]
        public MentionsAutoCompleteSource(ITeamExplorerContext teamExplorerContext, 
            IGraphQLClientFactory graphqlFactory,
            IAvatarProvider avatarProvider)
        {
            Guard.ArgumentNotNull(teamExplorerContext, nameof(teamExplorerContext));
            Guard.ArgumentNotNull(graphqlFactory, nameof(graphqlFactory));
            Guard.ArgumentNotNull(avatarProvider, nameof(avatarProvider));

            this.teamExplorerContext = teamExplorerContext;
            this.graphqlFactory = graphqlFactory;
            this.avatarProvider = avatarProvider;
        }

        public IObservable<AutoCompleteSuggestion> GetSuggestions()
        {
            var localRepositoryModel = teamExplorerContext.ActiveRepository;

            var hostAddress = HostAddress.Create(localRepositoryModel.CloneUrl.Host);
            var owner = localRepositoryModel.Owner;
            var name = localRepositoryModel.Name;

            if (query == null)
            {
                query = new Query().Repository(owner: Var(nameof(owner)), name: Var(nameof(name)))
                .Select(repository =>
                    repository.MentionableUsers(null, null, null, null)
                        .AllPages()
                        .Select(sourceItem => 
                            new SuggestionItem(sourceItem.Login, 
                                sourceItem.Name ?? "(unknown)", 
                                GetUrlSafe(sourceItem.AvatarUrl(null))))
                        .ToList())
                .Compile();
            }

            var variables = new Dictionary<string, object>
            {
                {nameof(owner), owner },
                {nameof(name), name },
            };

            return Observable.FromAsync(async () =>
            {
                var connection = await graphqlFactory.CreateConnection(hostAddress);
                var suggestions = await connection.Run(query, variables);
                return suggestions.Select(suggestion => new AutoCompleteSuggestion(suggestion.Name,
                    suggestion.Description,
                    ResolveImage(suggestion.IconKey.ToString()),
                    Prefix));
            }).SelectMany(enumerable => enumerable);
        }

        private IObservable<BitmapSource> ResolveImage(string uri) => avatarProvider.GetAvatar(uri);

        public string Prefix => "@";

        static Uri GetUrlSafe(string url)
        {
            Uri uri;
            Uri.TryCreate(url, UriKind.Absolute, out uri);
            return uri;
        }
    }
}
