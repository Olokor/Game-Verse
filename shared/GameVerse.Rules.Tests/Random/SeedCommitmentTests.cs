using System.Text;
using GameVerse.Rules.Random;

namespace GameVerse.Rules.Tests.Random;

public class SeedCommitmentTests
{
    private static byte[] Secret() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Sha256_matches_nist_vector()
    {
        SeedCommitment.ToHex(SeedCommitment.Sha256(Encoding.ASCII.GetBytes("abc")))
            .ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Commit_and_seed_match_golden_values()
    {
        // Golden values computed independently with Python hashlib.
        SeedCommitment.Commit(Secret()).ShouldBe("630dcd2966c4336691125448bbb25b4ff412a49c732db2c8abc1b8581bd710dd");
        SeedCommitment.DeriveSeed(Secret()).ShouldBe(15318436679847900320UL);
    }

    [Fact]
    public void Verify_accepts_only_the_committed_secret()
    {
        var commitment = SeedCommitment.Commit(Secret());
        SeedCommitment.Verify(commitment, Secret()).ShouldBeTrue();
        SeedCommitment.Verify(commitment.ToUpperInvariant(), Secret()).ShouldBeTrue();

        var other = Secret();
        other[31] ^= 1;
        SeedCommitment.Verify(commitment, other).ShouldBeFalse();
        SeedCommitment.Verify(null!, Secret()).ShouldBeFalse();
        SeedCommitment.Verify(commitment, null!).ShouldBeFalse();
        SeedCommitment.Verify(commitment, new byte[16]).ShouldBeFalse();
    }

    [Fact]
    public void Seed_is_not_derivable_from_the_commitment_alone()
    {
        // The seed uses a domain-separated hash, so it differs from the published commitment's bytes.
        var commitmentPrefix = Convert.FromHexString(SeedCommitment.Commit(Secret()))[..8];
        BitConverter.ToUInt64(commitmentPrefix).ShouldNotBe(SeedCommitment.DeriveSeed(Secret()));
    }

    [Fact]
    public void Bad_secrets_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => SeedCommitment.Commit(null!));
        Should.Throw<ArgumentException>(() => SeedCommitment.Commit(new byte[31]));
        Should.Throw<ArgumentException>(() => SeedCommitment.DeriveSeed(new byte[33]));
        Should.Throw<ArgumentNullException>(() => SeedCommitment.Sha256(null!));
        Should.Throw<ArgumentNullException>(() => SeedCommitment.ToHex(null!));
    }
}
