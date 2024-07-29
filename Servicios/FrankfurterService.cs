namespace apiFrankfurter.Servicios;
using System.Net.Http.Json;
using apiFrankfurter.Entidades;


public class FrankfurterService
{
    private readonly HttpClient _httpClient;

    public FrankfurterService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<object> GetLatestRates()
    {
        return await _httpClient.GetFromJsonAsync<object>("latest");
    }
}

record UserDto(string Username, string Password);