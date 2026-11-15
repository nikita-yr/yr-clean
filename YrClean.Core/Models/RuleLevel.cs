namespace YrClean.Core.Models;

// How much trust a cleanup source deserves. Scheduled runs only ever touch Safe.
public enum RuleLevel
{
    Safe,
    Review,
    Expert
}
