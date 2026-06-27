# M1 Identity Core Registration

M1.2 registers ASP.NET Core Identity in Infrastructure with `ApplicationIdentityUser`,
`IdentityRole<Guid>`, EF stores on `SouthBaySoccerDbContext`, a data-protection token provider,
EF-backed Data Protection keys, and `IdentityService : IIdentityService`. Functions composes
Infrastructure from `ConnectionStrings:SouthBaySoccerDb`; migrations still do not run at cold start.
