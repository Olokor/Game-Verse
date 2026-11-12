import { describe, expect, it, vi, afterEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import * as api from '../data/api';
import { mockHomeFeed } from '../data/mock/homeFeed';
import { renderApp } from '../test/render';

afterEach(() => vi.restoreAllMocks());

describe('Home', () => {
  it('shows skeletons, then every section', async () => {
    renderApp('/');
    expect(screen.getByRole('status', { name: 'Loading home' })).toHaveAttribute('aria-busy', 'true');

    expect(await screen.findByRole('link', { name: 'Play Ludo' })).toBeInTheDocument();
    for (const name of ['Continue playing', 'Daily quests', 'Categories', 'Friends online']) {
      expect(screen.getByRole('heading', { name })).toBeInTheDocument();
    }
    expect(screen.queryByLabelText('Loading home')).toBeNull();
  });

  it('lists only friends who are online or in a game', async () => {
    renderApp('/');
    const friends = await screen.findByRole('region', { name: 'Friends online' });
    expect(within(friends).getByText('Adaeze')).toBeInTheDocument();
    expect(within(friends).queryByText('Emeka')).toBeNull();
    expect(within(friends).getByText('4 of 5 online')).toBeInTheDocument();
  });

  it('claiming a quest marks it claimed and adds the reward', async () => {
    renderApp('/');
    const quests = await screen.findByRole('region', { name: 'Daily quests' });
    const claimButtons = within(quests).getAllByRole('button', { name: /^Claim/ });
    expect(claimButtons).toHaveLength(1);
    await userEvent.click(claimButtons[0]!);
    expect(within(quests).queryByRole('button', { name: /^Claim/ })).toBeNull();
    expect(within(quests).getAllByText('Claimed')).toHaveLength(2);
  });

  it('shows empty states when there is nothing to show', async () => {
    vi.spyOn(api.homeApi, 'getHomeFeed').mockResolvedValue({
      ...structuredClone(mockHomeFeed), featured: undefined, continuePlaying: [], quests: [], friends: [], categories: [],
    });
    renderApp('/');
    expect(await screen.findByText('Nothing in progress')).toBeInTheDocument();
    expect(screen.getByText('No categories yet')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /^Play / })).toBeNull();
    expect(screen.getByText('All done for today')).toBeInTheDocument();
    expect(screen.getByText('Play with friends')).toBeInTheDocument();
  });

  it('features the first playable catalog game and lists the rest as coming soon', async () => {
    renderApp('/');
    expect(await screen.findByRole('link', { name: 'Play Ludo' })).toBeInTheDocument();
    const coming = screen.getByRole('region', { name: 'More games coming' });
    expect(within(coming).getByText('10 on the way')).toBeInTheDocument();
    expect(within(coming).getByLabelText('Whot, coming soon')).toBeInTheDocument();
    expect(within(coming).queryAllByRole('link', { name: /Whot/ })).toHaveLength(0);
    const categories = screen.getByRole('region', { name: 'Categories' });
    expect(within(categories).getByRole('link', { name: /^Board\s*2 games$/ })).toBeInTheDocument(); // Ludo + Draughts
    expect(within(categories).getByRole('link', { name: /^Cards\s*1 game$/ })).toBeInTheDocument();
  });

  it('shows the error state when the catalog API fails', async () => {
    vi.stubGlobal('fetch', async () => new Response('{}', { status: 503 }));
    renderApp('/');
    expect(await screen.findByText("Couldn't load your home feed")).toBeInTheDocument();
  });

  it('shows an error state that retries', async () => {
    const spy = vi.spyOn(api.homeApi, 'getHomeFeed').mockRejectedValueOnce(new Error('offline'));
    renderApp('/');
    expect(await screen.findByText("Couldn't load your home feed")).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('link', { name: 'Play Ludo' })).toBeInTheDocument();
    expect(spy).toHaveBeenCalledTimes(2);
  });
});

describe('Games page', () => {
  it('lists playable games first, then coming soon, from the catalog', async () => {
    renderApp('/games');
    expect(await screen.findByRole('heading', { level: 1, name: 'Games' })).toBeInTheDocument();
    const playable = await screen.findByRole('region', { name: 'Play now' });
    expect(within(playable).getAllByRole('link').map((l) => l.getAttribute('href'))).toEqual(['/games/ludo']);
    const coming = screen.getByRole('region', { name: 'Coming soon' });
    expect(within(coming).getAllByLabelText(/coming soon$/)).toHaveLength(10);
  });

  it('filters by category from the query string', async () => {
    renderApp('/games?category=fight');
    expect(await screen.findByRole('heading', { level: 1, name: 'Fight games' })).toBeInTheDocument();
    expect(await screen.findByLabelText('Stickman Combat, coming soon')).toBeInTheDocument();
    expect(screen.queryByLabelText('Whot, coming soon')).toBeNull();
    expect(screen.queryByRole('region', { name: 'Play now' })).toBeNull();
  });
});

describe('Routes and shell', () => {
  it.each([
    ['/play', 'Play'],
    ['/social', 'Social'],
    ['/profile', 'Profile'],
    ['/games/ludo', 'Game lobby'],
    ['/nowhere', 'Page not found'],
  ])('%s renders a designed screen', async (path, heading) => {
    renderApp(path);
    expect(await screen.findByRole('heading', { level: 1, name: heading })).toBeInTheDocument();
  });

  it('has both navigations with five items and Play in the centre', async () => {
    renderApp('/');
    const navs = await screen.findAllByRole('navigation', { name: 'Main' });
    expect(navs).toHaveLength(2);
    for (const nav of navs) {
      const labels = within(nav).getAllByRole('link').filter((l) => !l.hasAttribute('aria-label')).map((l) => l.textContent?.trim());
      expect(labels).toEqual(['Home', 'Games', 'Play', 'Social', 'Profile']);
    }
  });

  it('serves the component gallery in development', async () => {
    renderApp('/dev/components');
    expect(await screen.findByRole('heading', { level: 1, name: 'Components' })).toBeInTheDocument();
  });
});
