import { TokenRotationChannel } from './token-rotation-channel';

let channelSeq = 0;
/** Two tabs of the app: separate channel instances on one channel name. */
const tabs = () => {
  const name = `test.token-rotation.${++channelSeq}`;
  return [new TokenRotationChannel(name), new TokenRotationChannel(name)] as const;
};

describe('TokenRotationChannel', () => {
  it("hands another tab the tokens a refresh token was exchanged for, readable only with that token", async () => {
    const [a, b] = tabs();
    await a.publish('spent-1', { authnResult: { refresh_token: 'next-1' } });

    await expect(b.waitForAnswer('spent-1', 1000)).resolves.toEqual({ authnResult: { refresh_token: 'next-1' } });
    await expect(b.answerFor('some-other-token')).resolves.toBeNull();
  });

  it('answers in the publishing tab too', async () => {
    const [a] = tabs();
    await a.publish('spent-2', { authzData: 'at' });

    await expect(a.answerFor('spent-2')).resolves.toEqual({ authzData: 'at' });
  });

  it('tells listeners about rotations from other tabs only, by an id that matches the spent token', async () => {
    const [a, b] = tabs();
    const fromOthersSeenByA: string[] = [];
    const seenByB: string[] = [];
    a.onRotation((id) => fromOthersSeenByA.push(id));
    b.onRotation((id) => seenByB.push(id));
    await a.publish('spent-3', {});
    await vi.waitFor(() => expect(seenByB).toHaveLength(1));

    expect(fromOthersSeenByA).toEqual([]);
    await expect(b.isSpentBy('spent-3', seenByB[0])).resolves.toBe(true);
    await expect(b.isSpentBy('spent-4', seenByB[0])).resolves.toBe(false);
  });

  it('gives up waiting when no tab redeems the token', async () => {
    const [, b] = tabs();
    await expect(b.waitForAnswer('never-spent', 50)).resolves.toBeNull();
  });

  it('lets the redeeming tab wait until its rotation is published', async () => {
    const [a] = tabs();
    let published = false;
    const waiting = a.awaitPublished('spent-5', 5000).then(() => (published = true));
    await new Promise((r) => setTimeout(r, 10));
    expect(published).toBe(false);
    await a.publish('spent-5', {});
    await waiting;
    expect(published).toBe(true);
  });
});
