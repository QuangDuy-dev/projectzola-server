using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySocialApp.Application.DTOs.Categories;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CategoryDto>>>> GetAll()
    {
        var result = await _categoryService.GetAllAsync();
        return Ok(ApiResponse<List<CategoryDto>>.SuccessResponse(result));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("~/api/admin/categories")]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> Create([FromBody] CreateCategoryRequest request)
    {
        var result = await _categoryService.CreateAsync(request);
        return Created("", ApiResponse<CategoryDto>.SuccessResponse(result, "Category created."));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("~/api/admin/categories/{id}")]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> Update(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var result = await _categoryService.UpdateAsync(id, request);
        return Ok(ApiResponse<CategoryDto>.SuccessResponse(result));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("~/api/admin/categories/{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        await _categoryService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Category deactivated."));
    }
}
