[![NuGet Version](https://img.shields.io/nuget/v/OpenSkillSharp)](https://img.shields.io/nuget/v/OpenSkillSharp)
![Downloads](https://img.shields.io/nuget/dt/OpenSkillSharp)
![Tests](https://img.shields.io/github/actions/workflow/status/myssto/OpenSkillSharp/ci.yml?label=tests)
[![Coverage](https://img.shields.io/coverallsCoverage/github/myssto/OpenSkillSharp)](https://coveralls.io/github/myssto/OpenSkillSharp)
[![License](https://img.shields.io/github/license/myssto/OpenSkillSharp)](https://github.com/myssto/OpenSkillSharp/blob/master/LICENSE)

# Openskill

.NET/C# implementation of Weng-Lin Rating, as described at https://www.csie.ntu.edu.tw/~cjlin/papers/online_ranking/online_journal.pdf

## Installation

OpenSkillSharp is available for installation via NuGet:

```shell
# via the dotnet CLI
dotnet add package OpenSkillSharp

# via the Package Manager CLI
Install-Package OpenSkillSharp
```

## Usage

Start using the models by creating an instance of your desired model.

```cs
// Create a model with default parameters
PlackettLuce model = new();

// Or customize to your needs
ThurstoneMostellerPart model = new()
{
  Mu = 28.67,
  Sigma = 8.07,
  ...
}
```

### Ratings

"Player ratings" are represented as objects that implement `OpenSkillSharp.Domain.Rating.IRating`. These objects contain properties which represent a gaussian curve where `Mu` represents the _mean_, and `Sigma` represents the spread or standard deviation. Create these using your model:

```cs
PlackettLuce model = new();

IRating a1 = model.Rating();
>> {Rating} {Mu: 25, Sigma: 8.33148112355601}

IRating a2 = model.Rating(mu: 32.444, sigma: 5.123);
>> {Rating} {Mu: 32.444, Sigma: 5.123}

IRating b1 = model.Rating(mu: 43.381, sigma: 2.421);
>> {Rating} {Mu: 43.381, Sigma: 2.421}

IRating b2 = model.Rating(mu: 25.188, sigma: 6.211);
>> {Rating} {Mu: 25.188, Sigma: 6.211}
```

Ratings are updated using the `IOpenSkillModel.Rate()` method. This method takes a list of teams from a game and produces a new list of teams containing updated ratings. If `a1` and `a2` play on a team and win against a team with players `b1` and `b2`, you can update their ratings like so:

```cs
List<ITeam> result = model.Rate(
  [
    new Team() { Players = [a1, a2] },
    new Team() { Players = [b1, b2] }
  ]
).ToList();

result[0].Players
>> {Rating} {Mu: 28.669648436582808, Sigma: 8.071520788025197}
>> {Rating} {Mu: 33.83086971107981, Sigma: 5.062772998705765}

result[1].Players
>> {Rating} {Mu: 43.071274808241974, Sigma: 2.4166900452721256}
>> {Rating} {Mu: 23.149503312339064, Sigma: 6.1378606973362135}
```

When displaying a rating or sorting a list of ratings you can use the `IRating.Ordinal` getter.

```cs
PlackettLuce model = new();
IRating player = model.Rating(mu: 43.07, sigma: 2.42);

player.Ordinal
>> 35.81
```

By default, this returns `mu - 3 * sigma`, reresenting a rating for which there is a [99.7%](https://en.wikipedia.org/wiki/68–95–99.7_rule) likelihood that the player's true rating is higher so with early games, a player's ordinal rating will usually rise, and can rise even if that player were to lose.

### Predicting Winners

For a given match of any number of teams, using `IOpenSkillModel.PredictWin()` will produce a list of relative odds that each of those teams will win.

```cs
PlackettLuce model = new();

Team t1 = new() { Players = [model.Rating()] };
Team t2 = new() { Players = [model.Rating(mu: 33.564, sigma: 1.123)] };

List<double> probabilities = model.PredictWin([t1, t2]).ToList();
>> { 0.45110899943132493, 0.5488910005686751 }

probabilities.Sum();
>> 1
```

### Predicting Draws

Similarly to win prediction, using `IOpenSkillModel.PredictDraw()` will produce a single number representing the relative chance that the given teams will draw their match. The probability here should be treated as relative to other matches, but in reality the odds of an actual legal draw will be impacted by some meta-function based on the rules of the game.

```cs
double prediction = model.PredictDraw([t1, t2]);
>> 0.09025530533015186
```

### Alternative Models

The recommended default model is **PlackettLuce**, which is a generalized Bradley-Terry model for _k_ >= 3 teams and typically scales the best. That considered, there are other models available with various differences between them.

-   Bradley-Terry rating models follow a logistic distribution over a player's skill, similar to Glicko.
-   Thurstone-Mosteller rating models follow a gaussian distribution similar to TrueSkill. Gaussian CDF/PDF functions differ in implementation from system to system and the accuracy of this model isn't typically as great as others but it can be tuned with custom gamma functions if you choose to do so.
-   Full pairing models should have more accurate ratings over partial pairing models, however in high _k_ games (for example a 100+ person marathon), Bradley-Terry and Thurstone-Mosteller models need to do a joint probability calculation which involves a computationally expensive _k_-1 dimensional integration. In the case where players only change based on their neighbors, partial pairing is desirable.

## References

This project is largely ported from the [openskill.py](https://github.com/vivekjoshy/openskill.py) package with changes made to bring the code style more in line with idiomatic C# principles. The vast majority of the unit tests and data for them were taken from the python package, as well as the [openskill.js](https://github.com/philihp/openskill.js) package. All of the Weng-Lin models are based off the research from this [paper](https://jmlr.org/papers/v12/weng11a.html) or are derivatives of the algorithms found in it.

-   Julia Ibstedt, Elsa Rådahl, Erik Turesson, and Magdalena vande Voorde. Application and further development of trueskill™ ranking in sports. 2019.
-   Ruby C. Weng and Chih-Jen Lin. A bayesian approximation method for online ranking. Journal of Machine Learning Research, 12(9):267–300, 2011. URL: http://jmlr.org/papers/v12/weng11a.html.

If you are struggling with any concepts or are looking for more in-depth usage documentation, it is highly recommended to take a look at the official python documentation [here](https://openskill.me/en/stable/), as the projects are incredibly similar in structure and you will find that most examples will apply to this library as well.

## Implementations in other Languages

-   [Javascript](https://github.com/philihp/openskill.js)
-   [Python](https://github.com/vivekjoshy/openskill.py)
-   [Elixir](https://github.com/philihp/openskill.ex)
-   [Java](https://github.com/pocketcombats/openskill-java)
-   [Kotlin](https://github.com/brezinajn/openskill.kt)
-   [Lua](https://github.com/bstummer/openskill.lua)
