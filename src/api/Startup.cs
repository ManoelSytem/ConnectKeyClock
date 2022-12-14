using System;
using System.IO;
using API.Authentication;
using API.Authorization.Decision;
using API.Authorization.RPT;
using API.Models;
using IdentityModel.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace API
{
    public class Startup
    {
        private const string RoleClaimType = "role";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpContextAccessor();

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin();
                });
            });
            services.AddControllers();
            services.AddDbContext<ApiDbContext>(options =>
            {
                var databasePath = Path.Combine(Path.GetTempPath(), "webinar-keycloak-authorization.db");
                options.UseSqlite($"Data Source={databasePath}");
            });

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1"
                    , new OpenApiInfo
                    {
                        Title = "API"
                        , Version = "v1"
                    });
            });

            // Configure authentication
            var jwtOptions = Configuration.GetSection("JwtBearer").Get<JwtBearerOptions>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(o =>
            {
                o.Authority = jwtOptions.Authority;
                o.Audience = jwtOptions.Audience; ;
                o.RequireHttpsMetadata = false;

                o.Events = new JwtBearerEvents()
                {
                    OnAuthenticationFailed = c =>
                    {
                        c.NoResult();

                        c.Response.StatusCode = 500;
                        c.Response.ContentType = "text/plain";

                        return c.Response.WriteAsync("An error occured processing your authentication.");
                    }
                };
            });


            // Configure authorization
            services.AddTransient<IClaimsTransformation>(_ =>
                  new KeycloakRolesClaimsTransformation(RoleClaimType, jwtOptions.Audience));

            // Configure authorization
            services.AddSingleton<IAuthorizationHandler, DecisionRequirementHandler>();
            services.AddSingleton<IAuthorizationHandler, RptRequirementHandler>();
            services.AddAuthorization(options =>
            {
                #region Decision Requirements

                options.AddPolicy("customers#read"
                    , builder => builder.AddRequirements(new DecisionRequirement("customers", "read"))
                );
            

                #endregion
            });

            services.AddHttpClient<KeycloakService>(client =>
            {
                client.BaseAddress = new Uri(Configuration["KeycloakResourceUrl"]);
            });
            services.AddHttpClient<TokenClient>();
            services.AddSingleton(_ =>
             Configuration.GetSection("ClientCredentialsTokenRequest").Get<ClientCredentialsTokenRequest>());
    }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"));
            }

            app.UseHttpsRedirection();
            app.UseCors();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}