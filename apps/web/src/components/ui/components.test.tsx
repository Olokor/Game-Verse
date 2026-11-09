import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Button } from './Button';
import { CurrencyPill } from './CurrencyPill';
import { XPBar } from './XPBar';
import { PlayerChip } from './PlayerChip';
import { GameCard } from './GameCard';
import { QuestCard, questState } from './QuestCard';
import { mockGames } from '../../data/mock/homeFeed';
import type { Quest } from '../../data/types';
import { renderUi } from '../../test/render';

describe('Button', () => {
  it('fires onClick by default', async () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Play</Button>);
    await userEvent.click(screen.getByRole('button', { name: 'Play' }));
    expect(onClick).toHaveBeenCalledOnce();
  });

  it('does not fire when disabled', async () => {
    const onClick = vi.fn();
    render(<Button disabled onClick={onClick}>Play</Button>);
    await userEvent.click(screen.getByRole('button', { name: 'Play' }));
    expect(onClick).not.toHaveBeenCalled();
  });

  it('is busy and ignores clicks while loading, keeping its label for screen readers', async () => {
    const onClick = vi.fn();
    render(<Button loading onClick={onClick}>Find match</Button>);
    const button = screen.getByRole('button', { name: 'Find match' });
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveAttribute('aria-disabled', 'true');
    await userEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();
  });

  it('does not submit a form while loading', async () => {
    const onSubmit = vi.fn((e: { preventDefault: () => void }) => e.preventDefault());
    render(
      <form onSubmit={onSubmit}>
        <Button type="submit" loading>Save</Button>
      </form>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it.each(['primary', 'secondary', 'ghost', 'danger'] as const)('renders the %s variant from token classes', (variant) => {
    render(<Button variant={variant}>{variant}</Button>);
    expect(screen.getByRole('button', { name: variant }).className).toMatch(/\b(bg|text)-(brand|bg|state|text)-/);
  });

  it('meets the 44px touch target at every size', () => {
    render(
      <>
        <Button size="sm">s</Button>
        <Button size="md">m</Button>
        <Button size="lg">l</Button>
      </>,
    );
    expect(screen.getByRole('button', { name: 's' }).className).toContain('h-touch-target');
    expect(screen.getByRole('button', { name: 'm' }).className).toContain('h-56');
    expect(screen.getByRole('button', { name: 'l' }).className).toContain('h-72');
  });
});

describe('CurrencyPill', () => {
  it('states the full amount and currency for assistive tech', () => {
    render(<CurrencyPill currency="coin" amount={12500} />);
    expect(screen.getByRole('img', { name: '12,500 coins' })).toHaveTextContent('12,500');
  });

  it('uses the singular for one', () => {
    render(<CurrencyPill currency="gem" amount={1} />);
    expect(screen.getByRole('img', { name: '1 gem' })).toBeInTheDocument();
  });

  it('becomes a button when it has an action', async () => {
    const onClick = vi.fn();
    render(<CurrencyPill currency="coin" amount={40} onClick={onClick} />);
    await userEvent.click(screen.getByRole('button', { name: '40 coins' }));
    expect(onClick).toHaveBeenCalledOnce();
  });

  it('shows the new amount after a change (no motion tokens in tests means no animation)', () => {
    const { rerender } = render(<CurrencyPill currency="coin" amount={100} />);
    rerender(<CurrencyPill currency="coin" amount={350} />);
    expect(screen.getByRole('img', { name: '350 coins' })).toHaveTextContent('350');
  });

  it('renders a skeleton while loading', () => {
    const { container } = render(<CurrencyPill currency="coin" amount={0} loading />);
    expect(screen.queryByRole('img')).toBeNull();
    expect(container.querySelector('[aria-hidden="true"]')).not.toBeNull();
  });
});

describe('XPBar', () => {
  it('exposes progress as a progressbar', () => {
    render(<XPBar level={12} xp={1240} xpRequired={2000} />);
    const bar = screen.getByRole('progressbar', { name: 'Level 12 progress' });
    expect(bar).toHaveAttribute('aria-valuenow', '1240');
    expect(bar).toHaveAttribute('aria-valuemax', '2000');
    expect(bar).toHaveAttribute('aria-valuetext', '1,240 of 2,000 XP');
    expect(screen.getByText('1,240 / 2,000 XP')).toBeInTheDocument();
  });

  it('hides the text line when compact', () => {
    render(<XPBar level={3} xp={10} xpRequired={100} compact />);
    expect(screen.queryByText(/XP$/)).toBeNull();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });
});

describe('PlayerChip', () => {
  it('shows rank and presence as text, not color alone', () => {
    render(<PlayerChip displayName="Adaeze" rank="diamond" presence="online" />);
    expect(screen.getByText('Adaeze')).toBeInTheDocument();
    expect(screen.getByText('Diamond')).toBeInTheDocument();
    expect(screen.getByText('Online')).toBeInTheDocument();
  });

  it('names the game for in-game friends', () => {
    render(<PlayerChip displayName="Tobi" rank="gold" presence="in-game" activity="Whot" />);
    expect(screen.getByText('In Whot')).toBeInTheDocument();
  });
});

describe('GameCard', () => {
  const whot = mockGames.whot!;

  it.each(['hero', 'standard', 'compact'] as const)('%s links to the game and shows its title', async (variant) => {
    renderUi(<GameCard game={whot} variant={variant} />);
    const link = await screen.findByRole('link');
    expect(link).toHaveAttribute('href', '/games/whot');
    expect(link).toHaveTextContent('Whot');
    expect(link).toHaveTextContent('New');
  });

  it('hero has a short accessible name, the tagline as description, and no nested button', async () => {
    renderUi(<GameCard game={whot} variant="hero" />);
    const link = await screen.findByRole('link', { name: 'Play Whot' });
    expect(link).toHaveAccessibleDescription(whot.tagline);
    expect(link).toHaveTextContent('8.4K playing');
    expect(link).toHaveTextContent('Play now');
    expect(link.querySelector('button')).toBeNull();
  });

  it('marks "your turn" with a chip, not only text', async () => {
    renderUi(<GameCard game={whot} subtitle="vs Tobi" yourTurn />);
    const link = await screen.findByRole('link');
    expect(within(link).getByText('Your turn')).toBeInTheDocument();
  });

  it('card art is decorative so the link name is not padded with "artwork"', async () => {
    renderUi(<GameCard game={whot} />);
    expect(await screen.findByRole('link')).not.toHaveTextContent('artwork');
    expect(screen.queryByRole('img', { name: /artwork/ })).toBeNull();
  });
});

describe('QuestCard', () => {
  const quest = (over: Partial<Quest>): Quest => ({
    id: 'q', title: 'Win 2 games', progress: 0, goal: 2, reward: { kind: 'coin', amount: 150 }, claimed: false, ...over,
  });

  it('derives state', () => {
    expect(questState(quest({ progress: 1 }))).toBe('active');
    expect(questState(quest({ progress: 2 }))).toBe('claimable');
    expect(questState(quest({ progress: 2, claimed: true }))).toBe('claimed');
  });

  it('offers Claim only when claimable', async () => {
    const onClaim = vi.fn();
    const { rerender } = render(<QuestCard quest={quest({ progress: 1 })} onClaim={onClaim} />);
    expect(screen.queryByRole('button', { name: /^Claim/ })).toBeNull();
    rerender(<QuestCard quest={quest({ progress: 2 })} onClaim={onClaim} />);
    await userEvent.click(screen.getByRole('button', { name: 'Claim 150 coins for Win 2 games' }));
    expect(onClaim).toHaveBeenCalledWith('q');
    rerender(<QuestCard quest={quest({ progress: 2, claimed: true })} onClaim={onClaim} />);
    expect(screen.getByText('Claimed')).toBeInTheDocument();
  });

  it('shows a failed claim as an alert and keeps Claim available', () => {
    render(<QuestCard quest={quest({ progress: 2 })} error />);
    expect(screen.getByRole('alert')).toHaveTextContent("Couldn't claim. Try again.");
    expect(screen.getByRole('button', { name: /^Claim/ })).toBeEnabled();
  });

  it('caps displayed progress at the goal', () => {
    render(<QuestCard quest={quest({ progress: 5 })} />);
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '2');
    expect(screen.getByText('2/2')).toBeInTheDocument();
  });
});
