using UnityEngine;
using UnityEngine.Rendering;
using ZXing;
using ZXing.Common;
using System;
using System.Threading;
using System.Collections.Generic;

namespace easyar
{
	[RequireComponent(typeof(Camera))]
	public class CameraImageRenderer : MonoBehaviour
	{
		public enum RenderType
		{
			Normal,
			Eyewear
		}

		public enum GlassesDisplay
		{
			Normal,
			Left,
			Right
		}

		private CommandBuffer commandBuffer;
		private ARMaterial arMat;
		private Material mat;
		private Camera targetCamera;
		private RenderTexture targetTexture;

		public RenderType Type = RenderType.Normal;
		public GlassesDisplay Display = GlassesDisplay.Normal;

		// VAR FOR ZXING //////////////
		
		internal class Data
		{
			public byte[] image;
			public int imageHeight;
			public int imageWidth;
			public int imageSize;
		}

		public string decodedResult;
		public string decodedFormat;
		BarcodeReader barcodeReader;
		Thread barcodeDecoderThread;
		TimeSpan barcodeThreadDelay;
		volatile bool stopSignal;
		volatile bool dataSignal;
		volatile Data data;
		
		///////////////////////////////		
		
		
		public void Start()
		{			
			// SELECT FORMATS TO DECODE //////		
			var formats = new List<BarcodeFormat>();
			formats.Add(BarcodeFormat.QR_CODE);
			
			// IF YOU NEED 1D CODE TO DECODE, REMOVE COMMENT BELOW OR ADD A CUSTOM BARCODEFORMAT //			
			// formats.Add(BarcodeFormat.All_1D);			
			
			barcodeReader = new BarcodeReader
			{
				AutoRotate = false,
				Options = new DecodingOptions
				{
					PossibleFormats = formats,
					TryHarder = true,
				}
			};

			data = new Data { image = null };
			dataSignal = true;
			stopSignal = false;
			
			// CALL NEW THREAD TO ZXING //	
      
			barcodeThreadDelay = TimeSpan.FromMilliseconds(1);
			barcodeDecoderThread = new Thread(DecodeQR);
			barcodeDecoderThread.Start();	
      
			//////////////////////////////
		}

		void DecodeQR()
		{
			while (!stopSignal)
			{				
				if (!dataSignal)
				{
						Debug.Log ("decode data");						
						Result result = barcodeReader.Decode (
							               data.image,
							               data.imageWidth,
							               data.imageHeight,
							               RGBLuminanceSource.BitmapFormat.Unknown);

						if (result != null) {
						
							//! HERE IS THE RESULT OF SCAN !//
						
							decodedResult = result.ToString ();
							decodedFormat = result.BarcodeFormat.ToString ();
						}

					dataSignal = true;
				}

				Thread.Sleep(barcodeThreadDelay);
			}
		}

		public void SetRenderType(RenderType value)
		{
			if (value == RenderType.Eyewear)
			{
				targetCamera.RemoveAllCommandBuffers();
			}
			else
			{
				UpdateCommandBuffer();
			}
			Type = value;
		}

		public RenderTexture TargetTexture
		{
			get
			{
				var screen_w = Screen.width;
				var screen_h = Screen.height;
				var w = screen_w * targetCamera.rect.width;
				var h = screen_h * targetCamera.rect.height;
				if (targetTexture == null)
				{
					targetTexture = new RenderTexture((int)w, (int)h, 0);
				}
				else
				{
					if ((int)w != targetTexture.width || (int)h != targetTexture.height)
					{
						Destroy(targetTexture);
						targetTexture = new RenderTexture((int)w, (int)h, 0);
						UpdateCommandBuffer();
					}
				}
				return targetTexture;
			}
		}

		private void UpdateCommandBuffer()
		{
			if (commandBuffer != null)
			{
				targetCamera.RemoveAllCommandBuffers();
				commandBuffer.Dispose();
				commandBuffer = new CommandBuffer();
				commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, mat);
				if (TargetTexture != null)
				{
					commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, TargetTexture);
				}
				targetCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
			}
		}

		private void Awake()
		{
			targetCamera = GetComponent<Camera>();
			arMat = new ARMaterial();
		}

		private void UpdateRender(easyar.Image image)
		{
			if (image == null)
			{
				return;
			}
			var updateMat = arMat.UpdateByImage(image);
			if (mat == updateMat)
			{
				return;
			}
			mat = updateMat;
			if (commandBuffer != null)
			{
				targetCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
				commandBuffer.Dispose();
				commandBuffer = null;
			}
			commandBuffer = new CommandBuffer();
			commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, mat);
			if (TargetTexture != null)
			{
				commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, TargetTexture);
			}
			if (Type == RenderType.Normal)
				targetCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);

		}

		byte[] allbyte;

		public string code
		{
			get { return  decodedResult; }
			set { decodedResult = value; }
		}

		public string format
		{
			get { return  decodedFormat; }
			set { decodedFormat = value; }
		}

		public void UpdateFrame(ARSessionUpdateEventArgs e)
		{
			var frame = e.IFrame;
			var image = frame.image();
			if (image != null)
			{
				var img = image;
				UpdateRender(img);

				var screenRotation = Utility.GetScreenRotation();
				var viewport_aspect_ratio = targetCamera.aspect;
				var projection = Utility.Matrix44FToMatrix4x4(e.CameraParam.projection(targetCamera.nearClipPlane, targetCamera.farClipPlane, viewport_aspect_ratio, screenRotation, true, false));

				var imageProjection = Utility.Matrix44FToMatrix4x4(e.CameraParam.imageProjection(viewport_aspect_ratio, screenRotation, true, false));
				targetCamera.projectionMatrix = projection * e.ImageRotationMatrixGlobal.inverse;

				mat.SetMatrix("_TextureRotation", imageProjection);

				// HERE WE GET ALL BYTES FROM EASYAR AND TRANSFER TO ZXING TO DECODE //				
				
				byte[] allbyte  = new byte[img.buffer().size()];
				img.buffer ().copyToByteArray (0,allbyte,0,img.buffer().size());

				if ( dataSignal)
				{
					data.image = allbyte;
					data.imageHeight = img.height();
					data.imageWidth = img.width();
					data.imageSize = img.buffer ().size ();
					dataSignal = false;
				}
				
				///////////////////////////////////////////////////////////////////////	

				img.Dispose();
			}
			else
			{
				Debug.Log("image is null");
			}
		}

		private void OnDestroy()
		{
			if (commandBuffer != null)
			{
				targetCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
				commandBuffer.Dispose();
			}
			arMat.Dispose();
			Destroy(targetTexture);

			stopSignal = true;
			barcodeDecoderThread.Join();

		}
	}
}
