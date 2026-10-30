#include "engine/frame_capture.h"

#include <d3d9.h>
#include <wincodec.h>
#include <windows.h>
#include <wrl/client.h>

#include <atomic>
#include <cstring>
#include <mutex>

namespace {
    using DevTools::FrameCapture::Image;
    using Microsoft::WRL::ComPtr;

    constexpr int kBytesPerPixel = 4;

    std::mutex g_lock;
    std::promise<Image> g_promise;
    std::atomic<bool> g_wanted{false};

    Image Failed(const char* step) {
        Image image;
        image.error = step;
        return image;
    }

    Image Grab(IDirect3DDevice9* device) {
        ComPtr<IDirect3DSurface9> backBuffer;
        D3DSURFACE_DESC desc{};
        if (FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer)) ||
            FAILED(backBuffer->GetDesc(&desc))) {
            return Failed("the back buffer could not be read");
        }
        if (desc.Format != D3DFMT_X8R8G8B8 && desc.Format != D3DFMT_A8R8G8B8) {
            return Failed("the back buffer is not 8 bits a channel");
        }

        // GetRenderTargetData refuses a multisampled source, and StretchRect resolves one.
        ComPtr<IDirect3DSurface9> resolved;
        ComPtr<IDirect3DSurface9> copy;
        if (FAILED(device->CreateRenderTarget(desc.Width, desc.Height, desc.Format, D3DMULTISAMPLE_NONE, 0, FALSE,
                                              &resolved, nullptr)) ||
            FAILED(device->StretchRect(backBuffer.Get(), nullptr, resolved.Get(), nullptr, D3DTEXF_NONE)) ||
            FAILED(device->CreateOffscreenPlainSurface(desc.Width, desc.Height, desc.Format, D3DPOOL_SYSTEMMEM,
                                                       &copy, nullptr)) ||
            FAILED(device->GetRenderTargetData(resolved.Get(), copy.Get()))) {
            return Failed("the back buffer could not be copied");
        }

        D3DLOCKED_RECT locked{};
        if (FAILED(copy->LockRect(&locked, nullptr, D3DLOCK_READONLY))) {
            return Failed("the copy could not be locked");
        }

        Image image;
        image.width = static_cast<int>(desc.Width);
        image.height = static_cast<int>(desc.Height);
        const size_t stride = desc.Width * kBytesPerPixel;
        image.pixels.resize(stride * desc.Height);
        for (UINT row = 0; row < desc.Height; ++row) {
            std::memcpy(image.pixels.data() + row * stride,
                        static_cast<const uint8_t*>(locked.pBits) + row * locked.Pitch, stride);
        }
        copy->UnlockRect();
        return image;
    }

    std::wstring Wide(const std::string& text) {
        std::wstring wide(text.size(), L'\0');
        wide.resize(MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(),
                                        static_cast<int>(wide.size())));
        return wide;
    }
}

namespace DevTools::FrameCapture {

std::future<Image> Next() {
    std::lock_guard<std::mutex> held(g_lock);
    g_promise = std::promise<Image>();
    g_wanted = true;
    return g_promise.get_future();
}

void OnPresent(IDirect3DDevice9* device) {
    if (!g_wanted.exchange(false)) {
        return;
    }
    std::lock_guard<std::mutex> held(g_lock);
    g_promise.set_value(Grab(device));
}

std::string SavePng(const Image& image, const std::string& path) {
    ComPtr<IWICImagingFactory> factory;
    ComPtr<IWICBitmap> bitmap;
    ComPtr<IWICStream> stream;
    ComPtr<IWICBitmapEncoder> encoder;
    ComPtr<IWICBitmapFrameEncode> frame;
    WICPixelFormatGUID format = GUID_WICPixelFormat24bppBGR;
    const bool written =
        SUCCEEDED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory))) &&
        SUCCEEDED(factory->CreateBitmapFromMemory(image.width, image.height, GUID_WICPixelFormat32bppBGR,
                                                  image.width * kBytesPerPixel, static_cast<UINT>(image.pixels.size()),
                                                  const_cast<BYTE*>(image.pixels.data()), &bitmap)) &&
        SUCCEEDED(factory->CreateStream(&stream)) &&
        SUCCEEDED(stream->InitializeFromFilename(Wide(path).c_str(), GENERIC_WRITE)) &&
        SUCCEEDED(factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder)) &&
        SUCCEEDED(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache)) &&
        SUCCEEDED(encoder->CreateNewFrame(&frame, nullptr)) && SUCCEEDED(frame->Initialize(nullptr)) &&
        SUCCEEDED(frame->SetPixelFormat(&format)) && SUCCEEDED(frame->WriteSource(bitmap.Get(), nullptr)) &&
        SUCCEEDED(frame->Commit()) && SUCCEEDED(encoder->Commit());
    return written ? std::string() : "the PNG could not be written to " + path;
}

}
